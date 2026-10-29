using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.IntegrationTests;
using Microsoft.Extensions.Configuration;
using System.Xml.Linq;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "../../../.."));
var results = new List<string>();
var failures = 0;
void Check(string name, bool ok) { results.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
var secretId = XDocument.Load(Path.Combine(root,"StoreFlow.Аpi","StoreFlow.Аpi.csproj")).Descendants("UserSecretsId").Single().Value;
var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(root,"StoreFlow.Аpi","appsettings.json"))
    .AddUserSecrets(secretId)
    .AddEnvironmentVariables().Build();
var originalConnection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Налаштуйте підключення PostgreSQL у User Secrets або ConnectionStrings__DefaultConnection.");
var databaseName = "storeflow_orders_qa_" + Guid.NewGuid().ToString("N");
var connection = new NpgsqlConnectionStringBuilder(originalConnection) { Database = databaseName }.ConnectionString;
var jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
const string adminEmail = "admin@qa.example.invalid";
const string adminPassword = "StoreFlow-Test#Admin-2026!";
var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(originalConnection) { Database = "postgres" }.ConnectionString);
Process? api = null;
var created = false;
var apiLogs = new System.Collections.Concurrent.ConcurrentQueue<string>();
Directory.CreateDirectory(Path.Combine(root, "StoreFlow.IntegrationTests", "results"));
var output = Path.Combine(root, "StoreFlow.IntegrationTests", "results");
foreach (var reportName in new[] { "orders.txt", "catalog.txt", "auth.txt", "validation.txt", "search.txt", "hardening.txt" })
    File.Delete(Path.Combine(output, reportName));
try
{
    var publicSettings = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root,"StoreFlow.Аpi","appsettings.json")))!;
    Check("Public PostgreSQL configuration has no password",
        string.IsNullOrEmpty(new NpgsqlConnectionStringBuilder(publicSettings["ConnectionStrings"]!["DefaultConnection"]!.GetValue<string>()).Password));
    Check("Public configuration has no JWT key", string.IsNullOrEmpty(publicSettings["Jwt"]?["Key"]?.GetValue<string>()));
    await admin.OpenAsync();
    await new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin).ExecuteNonQueryAsync();
    created = true;
    await using (var context = new StoreFlowDbContext(new DbContextOptionsBuilder<StoreFlowDbContext>().UseNpgsql(connection).Options))
    {
        // Спочатку відтворюємо стару схему, потім перевіряємо міграцію на даних.
        await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261026202126_InitialCreate");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Categories" ("Id","Name") VALUES (900000,'Migration check');
            INSERT INTO "Products" ("Id","Name","Price","StockQuantity","CategoryId") VALUES (900000,'Migration check',1,1,900000);
            INSERT INTO "Users" ("Id","FirstName","LastName","Email","PasswordHash","Role","CreatedAt")
            VALUES (900000,'Legacy','One',' Legacy@Example.Invalid ','legacy-hash','Customer',now()),
                   (900001,'Legacy','Two','SECOND@Example.Invalid','legacy-hash','Customer',now());
            """);
        await context.Database.MigrateAsync();
        Check("Migration preserves existing category and product", await context.Products.AnyAsync(x => x.Id == 900000));
        Check("Migration backfills existing user emails",
            await context.Users.AnyAsync(x => x.Id == 900000 && x.NormalizedEmail == "legacy@example.invalid") &&
            await context.Users.AnyAsync(x => x.Id == 900001 && x.NormalizedEmail == "second@example.invalid"));
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "Products" WHERE "Id"=900000; DELETE FROM "Categories" WHERE "Id"=900000;""");
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "Users" WHERE "Id" IN (900000,900001);""");
        Check("No pending model changes", !context.Database.HasPendingModelChanges());
        await context.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(root,"scripts","migrate-catalog.sql")));
        Check("Generated SQL migration script is idempotent", !(await context.Database.GetPendingMigrationsAsync()).Any());
    }
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var baseUrl = $"http://localhost:{port}";
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "StoreFlow.Аpi"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(TestPaths.ApiDll(root));
    start.ArgumentList.Add("--urls"); start.ArgumentList.Add(baseUrl);
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    start.Environment["ConnectionStrings__DefaultConnection"] = connection;
    start.Environment["Jwt__Key"] = jwtKey;
    start.Environment["Auth__RequestsPerMinute"] = "1000";
    start.Environment["BootstrapAdmin__Email"] = adminEmail;
    start.Environment["BootstrapAdmin__Password"] = adminPassword;
    start.ArgumentList.Add("--migrate");
    using (var migration = Process.Start(start)!)
    {
        var migrationOut = migration.StandardOutput.ReadToEndAsync();
        var migrationErr = migration.StandardError.ReadToEndAsync();
        await migration.WaitForExitAsync();
        await Task.WhenAll(migrationOut,migrationErr);
        Check("Container migration command is idempotent",migration.ExitCode==0);
        if (migration.ExitCode!=0) throw new Exception("Migration CLI failed.");
    }
    start.ArgumentList.Remove("--migrate");
    start.ArgumentList.Add("--bootstrap-admin");
    using (var bootstrap = Process.Start(start)!)
    {
        var bootstrapOut = bootstrap.StandardOutput.ReadToEndAsync();
        var bootstrapErr = bootstrap.StandardError.ReadToEndAsync();
        await bootstrap.WaitForExitAsync();
        await Task.WhenAll(bootstrapOut, bootstrapErr);
        Check("Local bootstrap creates first Admin", bootstrap.ExitCode == 0);
        if (bootstrap.ExitCode != 0) throw new Exception("Admin bootstrap failed.");
    }
    start.ArgumentList.Remove("--bootstrap-admin");
    start.Environment.Remove("BootstrapAdmin__Password");
    api = Process.Start(start)!;
    api.OutputDataReceived += (_, e) => { if (e.Data != null) apiLogs.Enqueue(e.Data); };
    api.ErrorDataReceived += (_, e) => { if (e.Data != null) apiLogs.Enqueue(e.Data); };
    api.BeginOutputReadLine(); api.BeginErrorReadLine();
    using var client = new HttpClient(new ContractHandler()) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    bool ready = false;
    for (int attempt = 0; attempt < 100; attempt++)
    {
        try { using var response = await client.GetAsync("/api/orders"); if (response.StatusCode == HttpStatusCode.Unauthorized) { ready = true; break; } }
        catch (HttpRequestException) { }
        if (api.HasExited) break;
        await Task.Delay(100);
    }
    if (!ready) throw new Exception("Test API did not start.");
    using (var health = await client.GetAsync("/health"))
        Check("Anonymous readiness checks PostgreSQL",health.IsSuccessStatusCode);
    using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = adminEmail, password = adminPassword });
    Check("Bootstrap admin can log in", login.IsSuccessStatusCode);
    var adminToken = JsonNode.Parse(await login.Content.ReadAsStringAsync())!["accessToken"]!.GetValue<string>();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    Check("Swagger includes Orders and excludes WeatherForecast",
        (await client.GetStringAsync("/swagger/v1/swagger.json")).Contains("/api/orders") &&
        !(await client.GetStringAsync("/swagger/v1/swagger.json")).Contains("weatherforecast", StringComparison.OrdinalIgnoreCase));
    Check("Manager panel and assets are served", (await client.GetStringAsync("/")).Contains("Панель менеджера") && (await client.GetStringAsync("/panel.js")).Contains("use strict") && (await client.GetStringAsync("/panel.css")).Contains("@media"));
    var swagger = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;
    Check("Swagger has PUT order status endpoint", swagger["paths"]!["/api/orders/{id}/status"]?["put"] != null);
    var productOperation = swagger["paths"]!["/api/products"]!["get"]!;
    Check("Swagger correctly marks public Products access",productOperation["security"] is JsonArray security &&
        security.All(x=>x!.AsObject().Count==0));
    var sortParameter = productOperation["parameters"]!.AsArray().Single(x=>string.Equals(x!["name"]!.GetValue<string>(),"SortBy",StringComparison.OrdinalIgnoreCase))!;
    Check("Swagger documents optional sort default",sortParameter["schema"]?["default"]?.GetValue<string>()=="id" && !(sortParameter["required"]?.GetValue<bool>()??false));
    Check("Swagger exposes requested order DTOs",
        swagger["components"]!["schemas"]?["CreateOrderDto"] != null &&
        swagger["components"]!["schemas"]?["CreateOrderItemDto"] != null &&
        swagger["components"]!["schemas"]?["OrderDto"] != null);
    failures += await CatalogSuite.RunAsync(baseUrl, connection, Path.Combine(output, "catalog.txt"), adminToken);

    await using var db = new NpgsqlConnection(connection);
    await db.OpenAsync();
    async Task<object?> Sql(string sql, params object[] values)
    {
        await using var cmd = new NpgsqlCommand(sql, db);
        foreach (var value in values) cmd.Parameters.AddWithValue(value);
        return await cmd.ExecuteScalarAsync();
    }
    async Task<JsonNode?> Call(string method, string path, object? body, int expected, bool raw = false)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body != null) request.Content = raw ? new StringContent((string)body, Encoding.UTF8, "application/json") : JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        Check(method + " " + path + " => " + expected + " (actual " + (int)response.StatusCode + ")", (int)response.StatusCode == expected);
        if ((int)response.StatusCode != expected) throw new Exception("Unexpected HTTP result: " + text);
        if (expected >= 400)
            Check("ProblemDetails status and no exception trace", JsonNode.Parse(text)?["status"]?.GetValue<int>() == expected && !text.Contains("StackTrace") && !text.Contains("Npgsql."));
        if (expected == 201) Check("Created Location", response.Headers.Location != null);
        if (expected == 204) Check("No content", text.Length == 0);
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }
    async Task<int> Stock(int id) => Convert.ToInt32(await Sql("SELECT \"StockQuantity\" FROM \"Products\" WHERE \"Id\"=$1", id));
    async Task<int> Product(int categoryId, string name, decimal price, int stock)
    {
        var product = await Call("POST", "/api/products", new { name, price, stockQuantity = stock, categoryId }, 201);
        return product!["id"]!.GetValue<int>();
    }
    int uid = Convert.ToInt32(await Sql("""
        INSERT INTO "Users" ("FirstName","LastName","Email","PasswordHash","Role","CreatedAt")
        VALUES ('Orders','QA','orders@example.invalid','not-a-login','Customer',now()) RETURNING "Id"
        """));
    int category = (await Call("POST", "/api/categories", new { name = "Orders QA" }, 201))!["id"]!.GetValue<int>();
    int a = await Product(category, "A", 10.25m, 10);
    int b = await Product(category, "B", 4.50m, 20);
    int c = await Product(category, "C", 2m, 10);
    object OrderBody(params (int id, int quantity)[] items) => new { userId = uid, items = items.Select(x => new { productId = x.id, quantity = x.quantity }) };
    foreach (var body in new object[] {
        new { userId=0, items=new[]{new{productId=a,quantity=1}} },
        new { userId=int.MaxValue, items=new[]{new{productId=a,quantity=1}} },
        new { userId=uid, items=Array.Empty<object>() },
        new { userId=uid, items=(object?)null },
        new { userId=uid, items=new object?[]{null} },
        new { userId=uid, items=Enumerable.Repeat(new{productId=a,quantity=1},101) },
        OrderBody((a,0)), OrderBody((a,-1)), OrderBody((0,1)), OrderBody((a,1),(a,2))
    }) await Call("POST","/api/orders",body,400);
    await Call("POST","/api/orders",OrderBody((int.MaxValue,1)),404);
    await Call("POST","/api/orders",OrderBody((a,1),(int.MaxValue,1)),404);
    await Call("POST","/api/orders","{}",400,true);
    await Call("POST","/api/orders","null",400,true);
    await Call("POST","/api/orders","{invalid",400,true);
    Check("Invalid requests create no orders",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Orders\" WHERE NOT \"IsDeleted\""))==0);
    Check("Invalid requests leave stock unchanged",await Stock(a)==10 && await Stock(b)==20);

    var order=await Call("POST","/api/orders",OrderBody((a,2),(b,3)),201);
    int oid=order!["id"]!.GetValue<int>();
    Check("Order starts Pending with UTC createdAt",order["status"]!.GetValue<string>()=="Pending" && DateTimeOffset.Parse(order["createdAt"]!.GetValue<string>()).Offset==TimeSpan.Zero);
    Check("Total 34 and persisted in PostgreSQL",order["totalAmount"]!.GetValue<decimal>()==34m && Convert.ToDecimal(await Sql("SELECT \"TotalAmount\" FROM \"Orders\" WHERE \"Id\"=$1",oid))==34m);
    Check("Stock reserved for all items",await Stock(a)==8 && await Stock(b)==17);
    var list=await Call("GET","/api/orders",null,200);
    Check("List includes order",list!.AsArray().Any(x=>x!["id"]!.GetValue<int>()==oid));
    await Call("GET","/api/orders/"+oid,null,200);
    int Item(JsonNode o,int pid)=>o["items"]!.AsArray().Single(x=>x!["productId"]!.GetValue<int>()==pid)!["id"]!.GetValue<int>();
    int ia=Item(order,a), ib=Item(order,b);
    await Sql("UPDATE \"Products\" SET \"Price\"=99 WHERE \"Id\"=$1",a);
    order=await Call("POST",$"/api/orders/{oid}/items",new{productId=a,quantity=1},200);
    Check("Adding existing product preserves price snapshot",order!["items"]!.AsArray().Single(x=>x!["productId"]!.GetValue<int>()==a)!["unitPrice"]!.GetValue<decimal>()==10.25m);
    Check("Adding existing item merges quantities",order["items"]!.AsArray().Count==2 && await Stock(a)==7);
    order=await Call("POST",$"/api/orders/{oid}/items",new{productId=c,quantity=2},200);
    int ic=Item(order!,c);
    Check("Total recalculated after add",order!["totalAmount"]!.GetValue<decimal>()==48.25m);
    order=await Call("PUT",$"/api/orders/{oid}/items/{ia}",new{quantity=1},200);
    Check("Reducing item returns stock and recalculates total",await Stock(a)==9 && order!["totalAmount"]!.GetValue<decimal>()==27.75m);
    order=await Call("PUT",$"/api/orders/{oid}/items/{ia}",new{quantity=2},200);
    Check("Increasing item reserves stock",await Stock(a)==8 && order!["totalAmount"]!.GetValue<decimal>()==38m);
    order=await Call("PUT",$"/api/orders/{oid}/items/{ia}",new{quantity=1},200);
    await Call("DELETE",$"/api/orders/{oid}/items/{ic}",null,200);
    Check("Removing item restores stock",await Stock(c)==10);
    await Call("DELETE",$"/api/orders/{oid}/items/{ib}",null,200);
    await Call("DELETE",$"/api/orders/{oid}/items/{ia}",null,409);
    await Call("PUT",$"/api/orders/{oid}/items/{ia}",new{quantity=0},400);
    await Call("PUT",$"/api/orders/{oid}/items/{ia}",new{quantity=int.MaxValue},400);
    await Call("PUT",$"/api/orders/{oid}/items/2147483647",new{quantity=1},404);
    await Call("DELETE",$"/api/orders/{oid}/items/2147483647",null,404);
    await Call("POST",$"/api/orders/{oid}/items",new{productId=int.MaxValue,quantity=1},404);
    await Call("POST",$"/api/orders/{oid}/items",new{productId=a,quantity=0},400);
    await Call("POST",$"/api/orders/{oid}/items",new{productId=a,quantity=int.MaxValue},400);
    Check("Failed item changes leave stock unchanged",await Stock(a)==9);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Completed"},409);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Unknown"},400);
    await Call("PUT",$"/api/orders/{oid}/status",new{status=0},400);
    await Call("PUT",$"/api/orders/{oid}/status",new{},400);
    await Call("POST",$"/api/orders/{oid}/items",new{productId=c,quantity=2},200);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Processing"},200);
    await Call("POST",$"/api/orders/{oid}/items",new{productId=a,quantity=1},409);
    await Call("PUT",$"/api/orders/{oid}/items/{ia}",new{quantity=2},409);
    await Call("DELETE",$"/api/orders/{oid}/items/{ia}",null,409);
    await Call("DELETE","/api/orders/"+oid,null,409);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Cancelled"},200);
    Check("Cancellation restores all remaining stock",await Stock(a)==10 && await Stock(b)==20 && await Stock(c)==10);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Cancelled"},200);
    Check("Repeated cancellation does not double restore",await Stock(a)==10 && await Stock(c)==10);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Processing"},409);
    await Call("DELETE","/api/orders/"+oid,null,204);
    Check("Deleting cancelled order does not change stock",await Stock(a)==10);
    Check("Deleting order preserves items for audit",Convert.ToInt64(await Sql("SELECT count(*) FROM \"OrderItems\" WHERE \"OrderId\"=$1",oid))>0);
    await Call("GET","/api/orders/"+oid,null,404);
    order=await Call("POST","/api/orders",OrderBody((a,2)),201);
    oid=order!["id"]!.GetValue<int>();
    Check("New order uses current product price",
        order["items"]![0]!["unitPrice"]!.GetValue<decimal>()==99m &&
        order["totalAmount"]!.GetValue<decimal>()==198m);
    await Call("DELETE","/api/orders/"+oid,null,204);
    Check("Deleting Pending order returns stock",await Stock(a)==10);
    await Call("POST","/api/orders",OrderBody((a,1),(b,21)),400);
    Check("Partial failure rolls back all stock and order inserts",await Stock(a)==10 && await Stock(b)==20 && Convert.ToInt64(await Sql("SELECT count(*) FROM \"Orders\" WHERE NOT \"IsDeleted\""))==0);

    order=await Call("POST","/api/orders",OrderBody((b,2)),201);
    oid=order!["id"]!.GetValue<int>();
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Processing"},200);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Shipped"},200);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Cancelled"},409);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Completed"},200);
    await Call("PUT",$"/api/orders/{oid}/status",new{status="Pending"},409);
    await Call("DELETE","/api/orders/"+oid,null,409);
    Check("Fulfillment does not deduct stock twice",await Stock(b)==18);
    await Call("DELETE","/api/products/"+b,null,409);
    foreach(var id in new[]{0,-1,int.MaxValue})
    {
        await Call("GET","/api/orders/"+id,null,404);
        await Call("DELETE","/api/orders/"+id,null,404);
        await Call("POST",$"/api/orders/{id}/items",new{productId=a,quantity=1},404);
        await Call("PUT",$"/api/orders/{id}/status",new{status="Processing"},404);
    }
    await Call("GET","/weatherforecast",null,404);
    await Call("GET","/api/orders/not-an-id",null,404);

    // Два запити конкурують за останню одиницю.
    int scarce=await Product(category,"Last unit",5,1);
    async Task<(int status,int id)> ConcurrentCreate()
    {
        using var res=await client.PostAsJsonAsync("/api/orders",OrderBody((scarce,1)));
        var node=JsonNode.Parse(await res.Content.ReadAsStringAsync());
        return ((int)res.StatusCode, res.IsSuccessStatusCode?node!["id"]!.GetValue<int>():0);
    }
    var competing=await Task.WhenAll(ConcurrentCreate(),ConcurrentCreate());
    Check("Concurrent orders: one 201 and one 400",competing.Count(x=>x.status==201)==1 && competing.Count(x=>x.status==400)==1);
    Check("Concurrent orders cannot oversell",await Stock(scarce)==0);
    int winner=competing.Single(x=>x.status==201).id;
    var cancels=await Task.WhenAll(
        client.PatchAsJsonAsync($"/api/orders/{winner}/status",new{status="Cancelled"}),
        client.PatchAsJsonAsync($"/api/orders/{winner}/status",new{status="Cancelled"}));
    Check("Concurrent cancellations both idempotent",cancels.All(x=>x.IsSuccessStatusCode));
    foreach(var response in cancels) response.Dispose();
    Check("Concurrent cancellation restores stock once",await Stock(scarce)==1);

    int shared=await Product(category,"Shared order",1,20);
    order=await Call("POST","/api/orders",OrderBody((shared,1)),201); oid=order!["id"]!.GetValue<int>();
    var adds=await Task.WhenAll(client.PostAsJsonAsync($"/api/orders/{oid}/items",new{productId=shared,quantity=2}),client.PostAsJsonAsync($"/api/orders/{oid}/items",new{productId=shared,quantity=3}));
    Check("Concurrent additions succeed",adds.All(x=>x.IsSuccessStatusCode));
    foreach(var response in adds) response.Dispose();
    Check("Concurrent additions do not lose quantities",Convert.ToInt32(await Sql("SELECT \"Quantity\" FROM \"OrderItems\" WHERE \"OrderId\"=$1",oid))==6 && await Stock(shared)==14);
    var deletes=await Task.WhenAll(client.DeleteAsync("/api/orders/"+oid),client.DeleteAsync("/api/orders/"+oid));
    Check("Concurrent deletes: one 204 and one 404",deletes.Count(x=>(int)x.StatusCode==204)==1 && deletes.Count(x=>(int)x.StatusCode==404)==1);
    foreach(var response in deletes) response.Dispose();
    Check("Concurrent delete restores stock once",await Stock(shared)==20);

    // Збій бази в ізольованому середовищі не має потрапляти у відповідь API.
    await Sql("ALTER TABLE \"Products\" RENAME TO \"Products_QA_Hidden\"");
    try
    {
        var problem=await Call("GET","/api/products",null,500);
        Check("500 has generic message and traceId",problem!["title"]!.GetValue<string>()=="Не вдалося обробити запит." && problem["traceId"]!=null);
    }
    finally { await Sql("ALTER TABLE \"Products_QA_Hidden\" RENAME TO \"Products\""); }
    await Call("GET","/api/products",null,200);
    Check("Stock remains nonnegative",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\" WHERE \"StockQuantity\"<0"))==0);
    Check("All persisted totals match item prices",Convert.ToInt64(await Sql("""
        SELECT count(*) FROM "Orders" o WHERE o."TotalAmount" <>
        (SELECT coalesce(sum(i."UnitPrice"*i."Quantity"),0) FROM "OrderItems" i WHERE i."OrderId"=o."Id")
        """))==0);
    var scriptStart = new ProcessStartInfo("pwsh") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    scriptStart.ArgumentList.Add("-NoProfile");
    scriptStart.ArgumentList.Add("-File");
    scriptStart.ArgumentList.Add(Path.Combine(root,"scripts","Test-CatalogApi.ps1"));
    scriptStart.ArgumentList.Add("-BaseUrl");
    scriptStart.ArgumentList.Add(baseUrl);
    scriptStart.Environment["STOREFLOW_ACCESS_TOKEN"] = adminToken;
    using (var script = Process.Start(scriptStart)!)
    {
        var stdout = script.StandardOutput.ReadToEndAsync();
        var stderr = script.StandardError.ReadToEndAsync();
        await script.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        Check("PowerShell catalog smoke script passes with JWT",script.ExitCode == 0 && (await stdout).Contains("PASS: 22 HTTP checks"));
    }
    failures += await ProductSearchSuite.RunAsync(baseUrl, connection, adminToken, Path.Combine(output, "search.txt"));
    failures += await ValidationSuite.RunAsync(baseUrl, connection, adminToken, Path.Combine(output, "validation.txt"));
    failures += await HardeningSuite.RunAsync(baseUrl, connection, adminToken, Path.Combine(output, "hardening.txt"));
    failures += await AuthSuite.RunAsync(root, baseUrl, connection, jwtKey, adminToken, adminEmail, adminPassword, Path.Combine(output, "auth.txt"));
}
catch(Exception error)
{
    Check("Test execution: "+error.GetType().Name+" "+error.Message,false);
    foreach(var line in apiLogs.TakeLast(70)) Console.WriteLine(line);
}
finally
{
    if(api is { HasExited: false }) { api.Kill(entireProcessTree:true); await api.WaitForExitAsync(); }
    api?.Dispose();
    NpgsqlConnection.ClearAllPools();
    if(created)
    {
        if(!databaseName.StartsWith("storeflow_orders_qa_") || databaseName.Length!=52) throw new Exception("Unexpected test database name: " + databaseName);
        await new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
        Check("Temporary test database removed",true);
    }
    await admin.DisposeAsync();
}
var catalogLines=File.Exists(Path.Combine(output,"catalog.txt"))?await File.ReadAllLinesAsync(Path.Combine(output,"catalog.txt")):[];
int catalogChecks=catalogLines.Count(x=>x.StartsWith("PASS ")||x.StartsWith("FAIL "));
var authLines=File.Exists(Path.Combine(output,"auth.txt"))?await File.ReadAllLinesAsync(Path.Combine(output,"auth.txt")):[];
int authChecks=authLines.Count(x=>x.StartsWith("PASS ")||x.StartsWith("FAIL "));
var validationLines=File.Exists(Path.Combine(output,"validation.txt"))?await File.ReadAllLinesAsync(Path.Combine(output,"validation.txt")):[];
int validationChecks=validationLines.Count(x=>x.StartsWith("PASS ")||x.StartsWith("FAIL "));
var searchLines=File.Exists(Path.Combine(output,"search.txt"))?await File.ReadAllLinesAsync(Path.Combine(output,"search.txt")):[];
int searchChecks=searchLines.Count(x=>x.StartsWith("PASS ")||x.StartsWith("FAIL "));
var hardeningLines = File.Exists(Path.Combine(output,"hardening.txt")) ? await File.ReadAllLinesAsync(Path.Combine(output,"hardening.txt")) : [];
int hardeningChecks = hardeningLines.Count(x => x.StartsWith("PASS ") || x.StartsWith("FAIL "));
results.Insert(0,$"Total: {results.Count + catalogChecks + authChecks + validationChecks + searchChecks + hardeningChecks} checks; Orders/middleware/lifecycle: {results.Count}; Catalog: {catalogChecks}; Auth: {authChecks}; Validation: {validationChecks}; Search: {searchChecks}; failures: {failures}");
await File.WriteAllLinesAsync(Path.Combine(output,"orders.txt"),results);
Console.WriteLine(results[0]);
foreach(var line in results.Where(x=>x.StartsWith("FAIL"))) Console.WriteLine(line);
return failures==0?0:1;


