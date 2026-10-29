using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;

namespace StoreFlow.IntegrationTests;

internal static class ValidationSuite
{
    public static async Task<int> RunAsync(string baseUrl, string connection, string token, string reportPath)
    {
        var checks = new List<string>();
        var failures = 0;
        void Check(string label, bool ok) { checks.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
        using var client = new HttpClient(new ContractHandler()) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        async Task<object?> Sql(string sql, params object[] values)
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            foreach (var value in values) cmd.Parameters.AddWithValue(value);
            return await cmd.ExecuteScalarAsync();
        }
        async Task<JsonNode?> Call(string method, string path, object? body, int expected, string? field = null, bool raw = false)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body != null) request.Content = raw ? new StringContent((string)body, Encoding.UTF8, "application/json") : JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Check(method + " " + path + " => " + expected, (int)response.StatusCode == expected);
            if ((int)response.StatusCode != expected) throw new Exception("Unexpected validation status " + (int)response.StatusCode);
            var node = text.Length == 0 ? null : JsonNode.Parse(text);
            if (field != null)
            {
                Check("Field error: " + field, node?["errors"]?[field] is JsonArray { Count: > 0 });
                Check("Validation response has status, title and traceId", node?["status"]?.GetValue<int>() == expected &&
                    node["title"]?.GetValue<string>() == "Помилка валідації." && node["traceId"] != null);
                Check("Validation messages are Ukrainian without exception details",
                    node!["errors"]!.AsObject().SelectMany(x => x.Value!.AsArray()).All(x =>
                        x!.GetValue<string>().Any(c => c is >= '\u0400' and <= '\u04ff')) &&
                    !text.Contains("System.") && !text.Contains("The JSON value"));
            }
            return node;
        }
        const string password = "Validation-Test#Password-2026!";
        var tag = "validation-" + Guid.NewGuid().ToString("N");
        var email = tag + "@example.invalid";
        var raceEmail = tag + "-race@example.invalid";
        var productIds = new List<int>(); int category = 0;
        var tables = new[] { "Products", "Categories", "Users", "Orders", "OrderItems" };
        var before = new Dictionary<string,long>();
        foreach (var table in tables) before[table] = Convert.ToInt64(await Sql($"SELECT count(*) FROM \"{table}\""));
        object Registration(string? address) => new { firstName = "Тест", lastName = "Валідація", email = address, password };
        try
        {
            foreach(var name in new string?[] { null, "", "   ", "\t\n" })
                await Call("POST", "/api/categories", new { name }, 400, "name");
            await Call("POST", "/api/categories", new { }, 400, "name");
            category = (await Call("POST", "/api/categories", new { name = "  Валідація  ", description = (string?)null }, 201))!["id"]!.GetValue<int>();
            Check("Valid name is trimmed", (string?)await Sql("SELECT \"Name\" FROM \"Categories\" WHERE \"Id\"=$1", category) == "Валідація");

            var valid = await Call("POST", "/api/products", new { name = "Мінімальна ціна", price = 0.0000000000000000000000000001m, stockQuantity = 0, categoryId = category }, 201);
            int product = valid!["id"]!.GetValue<int>(); productIds.Add(product);
            Check("Smallest positive decimal price and zero stock persist", Convert.ToDecimal(await Sql("SELECT \"Price\" FROM \"Products\" WHERE \"Id\"=$1",product)) == 0.0000000000000000000000000001m &&
                Convert.ToInt32(await Sql("SELECT \"StockQuantity\" FROM \"Products\" WHERE \"Id\"=$1",product)) == 0);
            foreach(var price in new decimal?[]{null,0,-1})
                await Call("POST","/api/products",new{name="Bad",price,stockQuantity=0,categoryId=category},400,"price");
            await Call("POST","/api/products",new{name="Bad",stockQuantity=0,categoryId=category},400,"price");
            await Call("POST","/api/products",new{name="Bad",price=1,categoryId=category},400,"stockQuantity");
            await Call("POST","/api/products",new{name="Bad",price=1,stockQuantity=(int?)null,categoryId=category},400,"stockQuantity");
            await Call("POST","/api/products",new{name="Bad",price=1,stockQuantity=-1,categoryId=category},400,"stockQuantity");
            await Call("POST","/api/products",new{name="Bad",price=1,stockQuantity=0},400,"categoryId");
            await Call("POST","/api/products",new{name="Bad",price=1,stockQuantity=0,categoryId=(int?)null},400,"categoryId");
            await Call("POST","/api/products",new{name="Bad",price=1,stockQuantity=0,categoryId=0},400,"categoryId");
            await Call("POST","/api/products",new{name=" ",price=1,stockQuantity=0,categoryId=category},400,"name");
            await Call("POST","/api/products","{\"name\":\"Bad\",\"price\":\"no-number\",\"stockQuantity\":0,\"categoryId\":"+category+"}",400,"price",true);
            await Call("POST","/api/products","{\"name\":\"Bad\",\"price\":1,\"stockQuantity\":2147483648,\"categoryId\":"+category+"}",400,"stockQuantity",true);
            await Call("POST","/api/products","{\"name\":\"Bad\",\"price\":1e100,\"stockQuantity\":0,\"categoryId\":"+category+"}",400,"price",true);
            await Call("POST","/api/products","null",400,"body",true);
            await Call("POST","/api/products","{broken",400,"body",true);

            foreach (var route in new[] { "/api/categories", "/api/products" })
            {
                await Call("POST", route, new { name = new string('x',201), price = 1, stockQuantity = 0, categoryId = category },400,"name");
                await Call("POST", route, new { name = "Long description", description = new string('x',4001), price = 1, stockQuantity = 0, categoryId = category },400,"description");
            }
            foreach (var table in new[] { "Categories", "Products" })
            foreach (var field in new[] { "Name", "Description" })
            {
                try {
                    await Sql($"UPDATE \"{table}\" SET \"{field}\"=$1 WHERE \"Id\"=$2", new string('x',field == "Name" ? 201 : 4001), table == "Categories" ? category : product);
                    Check("Database rejects oversized " + table + "." + field,false);
                }
                catch (PostgresException error) { Check("Database rejects oversized " + table + "." + field,error.SqlState == "23514"); }
            }
            var originalRow = Convert.ToString(await Sql("SELECT row_to_json(p)::text FROM \"Products\" p WHERE \"Id\"=$1", product));
            await Call("PUT","/api/products/"+product,new{name="Bad",price=0,stockQuantity=0,categoryId=category},400,"price");
            await Call("PUT","/api/products/"+product,new{name="Bad",price=1,categoryId=category},400,"stockQuantity");
            Check("Invalid PUT never changes PostgreSQL row", originalRow == Convert.ToString(await Sql("SELECT row_to_json(p)::text FROM \"Products\" p WHERE \"Id\"=$1",product)));
            foreach(var quantity in new[]{0,-1})
                await Call("POST","/api/orders",new{items=new[]{new{productId=product,quantity}}},400,"items[0].quantity");
            await Call("POST","/api/orders",new{items=new[]{new{productId=product}}},400,"items[0].quantity");
            await Call("POST","/api/orders",new{items=Array.Empty<object>()},400,"items");
            await Call("POST","/api/orders",new{items=(object?)null},400,"items");

            foreach(var price in new[]{0.009m,decimal.MaxValue})
            {
                var big = await Call("POST","/api/products",new{name="Додатна ціна",price,stockQuantity=int.MaxValue,categoryId=category},201);
                productIds.Add(big!["id"]!.GetValue<int>());
                Check("Every representable positive price is allowed", Convert.ToDecimal(await Sql("SELECT \"Price\" FROM \"Products\" WHERE \"Id\"=$1",productIds[^1]))==price);
            }
            var orderCount = Convert.ToInt64(await Sql("SELECT count(*) FROM \"Orders\""));
            await Call("POST","/api/orders",new{items=new[]{new{productId=productIds[^1],quantity=2}}},409);
            Check("Order total overflow rolls back stock and insert",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Orders\""))==orderCount &&
                Convert.ToInt32(await Sql("SELECT \"StockQuantity\" FROM \"Products\" WHERE \"Id\"=$1",productIds[^1]))==int.MaxValue);

            foreach(var invalidEmail in new string?[]{
                null,""," ","missing-at","a@","@example.com","a@@example.com",".a@example.com","a.@example.com","a..b@example.com",
                "a b@example.com","Name <a@example.com>","a@localhost","a@-example.com","a@example-.com","a@exam_ple.com",
                "a@example..com","a@example.com\r\n","a@example.com\nBcc:x@example.com",new string('a',65)+"@example.com"
            })
            {
                await Call("POST","/api/auth/register",Registration(invalidEmail),400,"email");
                await Call("POST","/api/auth/login",new{email=invalidEmail,password},400,"email");
            }
            await Call("POST","/api/auth/register",new{firstName="",lastName="QA",email,password},400,"firstName");
            await Call("POST","/api/auth/register",new{firstName="QA",lastName=" ",email,password},400,"lastName");
            await Call("POST","/api/auth/register",new{firstName="QA",lastName="QA",email,password=""},400,"password");
            await Call("POST","/api/auth/register",new{firstName="QA",lastName="QA",email,password="short"},400,"password");
            await Call("POST","/api/auth/register",new{firstName="QA",lastName="QA",email,password=new string('a',129)},400,"password");
            await Call("PUT","/api/users/me",new{firstName=" ",lastName="QA"},400,"firstName");
            await Call("PUT","/api/users/me/password",new{currentPassword="",newPassword=password},400,"currentPassword");
            await Call("PUT","/api/users/me/password",new{currentPassword=password,newPassword=""},400,"newPassword");
            await Call("PUT","/api/users/1/role",new{role=""},400,"role");

            var user=await Call("POST","/api/auth/register",Registration(email),201);
            var duplicate=await Call("POST","/api/auth/register",Registration("  "+email.ToUpperInvariant()+"  "),409,"email");
            var login=await Call("POST","/api/auth/login",new{email="  "+email.ToUpperInvariant()+"  ",password},200);
            Check("Padded mixed-case email logs into existing user",login!["user"]!["id"]!.GetValue<int>()==user!["user"]!["id"]!.GetValue<int>());
            var parallel=await Task.WhenAll(Enumerable.Range(0,4).Select(index =>
                client.PostAsJsonAsync("/api/auth/register",Registration(index%2==0?raceEmail:raceEmail.ToUpperInvariant()))));
            try
            {
                Check("Four concurrent registrations create exactly one user",parallel.Count(x=>(int)x.StatusCode==201)==1 && parallel.Count(x=>(int)x.StatusCode==409)==3);
                foreach(var response in parallel.Where(x=>(int)x.StatusCode==409))
                {
                    var conflict=JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
                    Check("Concurrent duplicate reports same email field error",conflict["errors"]?["email"]?[0]?.GetValue<string>()==duplicate!["errors"]!["email"]![0]!.GetValue<string>());
                }
                Check("PostgreSQL stores one normalized email",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Users\" WHERE \"NormalizedEmail\"=$1",raceEmail))==1);
            }
            finally { foreach(var response in parallel) response.Dispose(); }
            var swagger=JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;
            var required=swagger["components"]!["schemas"]!["ProductRequest"]!["required"]!.AsArray();
            Check("Swagger marks all required product fields",new[]{"name","price","stockQuantity","categoryId"}.All(field=>required.Any(x=>x!.GetValue<string>()==field)));
        }
        catch(Exception error) { Check("Validation execution: "+error.GetType().Name+" "+error.Message,false); }
        finally
        {
            await Sql("DELETE FROM \"Users\" WHERE \"NormalizedEmail\" IN ($1,$2)",email,raceEmail);
            foreach(var id in productIds) await Sql("DELETE FROM \"Products\" WHERE \"Id\"=$1",id);
            if(category!=0) await Sql("DELETE FROM \"Categories\" WHERE \"Id\"=$1",category);
            foreach(var table in tables)
                Check("Validation cleanup preserves "+table,Convert.ToInt64(await Sql($"SELECT count(*) FROM \"{table}\""))==before[table]);
        }
        checks.Insert(0,$"Validation: {checks.Count} checks; failures: {failures}");
        await File.WriteAllLinesAsync(reportPath,checks);
        Console.WriteLine(checks[0]);
        foreach(var line in checks.Where(x=>x.StartsWith("FAIL"))) Console.WriteLine(line);
        return failures;
    }
}
