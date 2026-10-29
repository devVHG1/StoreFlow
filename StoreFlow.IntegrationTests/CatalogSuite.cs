using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;

namespace StoreFlow.IntegrationTests;

internal static class CatalogSuite
{
public static async Task<int> RunAsync(string baseUrl, string connectionString, string reportPath, string accessToken)
{
var report = new List<string>();
var failures = 0;
void Check(string label, bool ok) { report.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
using var client = new HttpClient(new ContractHandler()) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(20) };
client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
await using var db = new NpgsqlConnection(connectionString);
await db.OpenAsync();
async Task<object?> Sql(string sql, params object[] args) {
    await using var cmd = new NpgsqlCommand(sql, db);
    foreach (var arg in args) cmd.Parameters.AddWithValue(arg);
    return await cmd.ExecuteScalarAsync();
}
async Task<JsonNode?> Api(string method, string path, object? body, int expected, string? label = null, bool raw = false) {
    using var req = new HttpRequestMessage(new HttpMethod(method), "/api/" + path);
    if (body != null) req.Content = raw ? new StringContent((string)body, Encoding.UTF8, "application/json") : JsonContent.Create(body);
    using var res = await client.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    Check(label ?? method + " " + path + " => " + expected, (int)res.StatusCode == expected);
    if (expected == 201 && (int)res.StatusCode == 201) Check("POST Location points to new resource", res.Headers.Location?.ToString().Contains("/" + JsonNode.Parse(text)!["id"]) == true);
    if (expected == 204) Check("204 has empty body", text.Length == 0);
    if ((int)res.StatusCode >= 400 && !(expected == 404 && path.EndsWith("/abc"))) Check("Error is JSON with matching status: " + method + " " + path, res.Content.Headers.ContentType?.MediaType?.Contains("json") == true && JsonNode.Parse(text)?["status"]?.GetValue<int>() == (int)res.StatusCode);
    return text.Length == 0 ? null : JsonNode.Parse(text);
}
var tag = "QA-" + Guid.NewGuid().ToString("N");
var cats = new List<int>(); var prods = new List<int>(); int user=0, order=0, item=0;
var tables = new[]{"Categories","Products","Users","Orders","OrderItems"};
var initial = new Dictionary<string,long>();
foreach(var t in tables) initial[t] = Convert.ToInt64(await Sql($"SELECT count(*) FROM \"{t}\""));
try {
    var c = await Api("POST","categories",new{name=tag+" Книги",description="Опис українською"},201);
    int cid=c!["id"]!.GetValue<int>(); cats.Add(cid);
    c=await Api("POST","categories",new{name=tag+" Other"},201); int c2=c!["id"]!.GetValue<int>(); cats.Add(c2);
    Check("PostgreSQL category values", (string?)await Sql("SELECT \"Description\" FROM \"Categories\" WHERE \"Id\"=$1",cid)=="Опис українською");
    var all=await Api("GET","categories",null,200);
    Check("Category list includes created ID",all!.AsArray().Any(x=>x!["id"]!.GetValue<int>()==cid));
    await Api("GET",$"categories/{cid}",null,200);
    await Api("PUT",$"categories/{cid}",new{name=tag+" Updated",description=(string?)null},204);
    Check("PostgreSQL category update", (string?)await Sql("SELECT \"Name\" FROM \"Categories\" WHERE \"Id\"=$1",cid)==tag+" Updated");
    var p=await Api("POST","products",new{name=tag+" Товар",description="Опис",price=12.3456m,stockQuantity=0,categoryId=cid},201);
    int pid=p!["id"]!.GetValue<int>();prods.Add(pid);
    Check("PostgreSQL exact decimal price",Convert.ToDecimal(await Sql("SELECT \"Price\" FROM \"Products\" WHERE \"Id\"=$1",pid))==12.3456m);
    all=await Api("GET","products",null,200);
    Check("Product list includes created ID",all!["items"]!.AsArray().Any(x=>x!["id"]!.GetValue<int>()==pid));
    await Api("GET",$"products/{pid}",null,200);
    await Api("PUT",$"products/{pid}",new{name=tag+" Changed",price=0.01m,stockQuantity=int.MaxValue,categoryId=c2},204);
    Check("PostgreSQL category reassignment and boundary values",Convert.ToInt32(await Sql("SELECT \"CategoryId\" FROM \"Products\" WHERE \"Id\"=$1",pid))==c2 && Convert.ToDecimal(await Sql("SELECT \"Price\" FROM \"Products\" WHERE \"Id\"=$1",pid))==0.01m && Convert.ToInt32(await Sql("SELECT \"StockQuantity\" FROM \"Products\" WHERE \"Id\"=$1",pid))==int.MaxValue);
    await Api("DELETE",$"categories/{c2}",null,409);
    Check("409 preserves category and product",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\" WHERE \"Id\"=$1",pid))==1 && Convert.ToInt64(await Sql("SELECT count(*) FROM \"Categories\" WHERE \"Id\"=$1",c2))==1);
    foreach(var route in new[]{"categories","products"}) {
        foreach(var name in new string?[]{null,""," ","\t\n"}) {
            var invalid=new{name,price=1,stockQuantity=0,categoryId=cid};
            await Api("POST",route,invalid,400,route+" rejects blank name POST");
            await Api("PUT",route+"/"+(route=="categories"?cid:pid),invalid,400,route+" rejects blank name PUT");
        }
        await Api("POST",route,"{}",400,route+" rejects empty object",true);
        await Api("POST",route,"null",400,route+" rejects null body",true);
        await Api("POST",route,"{broken",400,route+" rejects malformed JSON",true);
        foreach(var missing in new[]{0,-1,int.MaxValue}) {
            await Api("GET",$"{route}/{missing}",null,404);
            await Api("PUT",$"{route}/{missing}",new{name=tag,price=1,stockQuantity=0,categoryId=cid},404);
            await Api("DELETE",$"{route}/{missing}",null,404);
        }
        await Api("GET",route+"/abc",null,404);
    }
    foreach(var price in new[]{0m,-1m})
        await Api("POST","products",new{name=tag,price,stockQuantity=1,categoryId=cid},400,"Reject invalid price "+price);
    await Api("POST","products",new{name=tag,price=1,stockQuantity=-1,categoryId=cid},400);
    foreach(var categoryId in new[]{0,-1,int.MaxValue}) {
        await Api("POST","products",new{name=tag,price=1,stockQuantity=0,categoryId},400);
        await Api("PUT",$"products/{pid}",new{name=tag,price=1,stockQuantity=0,categoryId},400);
    }
    await Api("PUT",$"products/{pid}",new{name=tag,price=-1,stockQuantity=0,categoryId=cid},400);
    Check("Rejected PUT leaves row unchanged",Convert.ToDecimal(await Sql("SELECT \"Price\" FROM \"Products\" WHERE \"Id\"=$1",pid))==0.01m && Convert.ToInt32(await Sql("SELECT \"CategoryId\" FROM \"Products\" WHERE \"Id\"=$1",pid))==c2);
    foreach(var route in new[]{"categories","products"}) {
        var r=await Api("POST",route,new{name="  "+tag+" spaced  ",price=1,stockQuantity=0,categoryId=cid},201,route+" accepts surrounding spaces and trims");
        if(r?["id"] is JsonNode id) {
            (route=="categories"?cats:prods).Add(id.GetValue<int>());
            Check(route+" trims name",r["name"]!.GetValue<string>()==tag+" spaced");
        }
    }
    user=Convert.ToInt32(await Sql("INSERT INTO \"Users\" (\"FirstName\",\"LastName\",\"Email\",\"PasswordHash\",\"Role\",\"CreatedAt\") VALUES ($1,'QA',$2,'not-a-login','Customer',now()) RETURNING \"Id\"",tag,tag+"@example.invalid"));
    order=Convert.ToInt32(await Sql("INSERT INTO \"Orders\" (\"UserId\",\"CreatedAt\",\"Status\",\"TotalAmount\") VALUES ($1,now(),1,1) RETURNING \"Id\"",user));
    item=Convert.ToInt32(await Sql("INSERT INTO \"OrderItems\" (\"OrderId\",\"ProductId\",\"Quantity\",\"UnitPrice\") VALUES ($1,$2,1,1) RETURNING \"Id\"",order,pid));
    await Api("DELETE",$"products/{pid}",null,409,"Product used in order returns 409");
    Check("409 preserves order item",Convert.ToInt64(await Sql("SELECT count(*) FROM \"OrderItems\" WHERE \"Id\"=$1",item))==1);
    await Sql("DELETE FROM \"OrderItems\" WHERE \"Id\"=$1",item); item=0;
    await Api("DELETE",$"products/{pid}",null,204);
    Check("PostgreSQL product deleted",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\" WHERE \"Id\"=$1",pid))==0);
    await Api("GET",$"products/{pid}",null,404); await Api("DELETE",$"products/{pid}",null,404);
    await Api("DELETE",$"categories/{c2}",null,204);
    Check("PostgreSQL category deleted",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Categories\" WHERE \"Id\"=$1",c2))==0);
    await Api("GET",$"categories/{c2}",null,404); await Api("DELETE",$"categories/{c2}",null,404);
    // Відтворюємо одночасне видалення категорії та додавання товару.
    c=await Api("POST","categories",new{name=tag+" Race"},201);
    int raceCategory=c!["id"]!.GetValue<int>(); cats.Add(raceCategory);
    await using(var tx=await db.BeginTransactionAsync()) {
        await Sql("SELECT \"Id\" FROM \"Categories\" WHERE \"Id\"=$1 FOR UPDATE",raceCategory);
        var deletion=client.DeleteAsync("/api/categories/"+raceCategory);
        bool waiting=false;
        for(int attempt=0;attempt<50;attempt++) {
            waiting=Convert.ToInt64(await Sql("SELECT count(*) FROM pg_stat_activity WHERE $1 = ANY(pg_blocking_pids(pid)) AND query LIKE '%DELETE FROM%Categories%'",db.ProcessID))>0;
            if(waiting || deletion.IsCompleted) break;
            await Task.Delay(100);
        }
        Check("Concurrent category delete is blocked by held row lock",waiting || !deletion.IsCompleted);
        int raceProduct=Convert.ToInt32(await Sql("INSERT INTO \"Products\" (\"Name\",\"Price\",\"StockQuantity\",\"CategoryId\") VALUES ($1,1,1,$2) RETURNING \"Id\"",tag+" Race",raceCategory)); prods.Add(raceProduct);
        await tx.CommitAsync();
        using var deleted=await deletion;
        Check("Concurrent delete protects newly committed product (expected 409, actual " + (int)deleted.StatusCode + ")",(int)deleted.StatusCode==409);
        Check("Concurrent product remains in PostgreSQL",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\" WHERE \"Id\"=$1",raceProduct))==1);
    }
    // Та сама перевірка для товару, який додають до замовлення.
    p=await Api("POST","products",new{name=tag+" Order race",price=1,stockQuantity=1,categoryId=cid},201);
    int orderRaceProduct=p!["id"]!.GetValue<int>(); prods.Add(orderRaceProduct);
    await using(var tx=await db.BeginTransactionAsync()) {
        await Sql("SELECT \"Id\" FROM \"Products\" WHERE \"Id\"=$1 FOR UPDATE",orderRaceProduct);
        var deletion=client.DeleteAsync("/api/products/"+orderRaceProduct);
        bool waiting=false;
        for(int attempt=0;attempt<50;attempt++) {
            waiting=Convert.ToInt64(await Sql("SELECT count(*) FROM pg_stat_activity WHERE $1 = ANY(pg_blocking_pids(pid)) AND query LIKE '%DELETE FROM%Products%'",db.ProcessID))>0;
            if(waiting || deletion.IsCompleted) break;
            await Task.Delay(100);
        }
        Check("Concurrent product delete is blocked by held row lock",waiting || !deletion.IsCompleted);
        item=Convert.ToInt32(await Sql("INSERT INTO \"OrderItems\" (\"OrderId\",\"ProductId\",\"Quantity\",\"UnitPrice\") VALUES ($1,$2,1,1) RETURNING \"Id\"",order,orderRaceProduct));
        await tx.CommitAsync();
        using var deleted=await deletion;
        Check("Concurrent delete protects product used in order (expected 409, actual " + (int)deleted.StatusCode + ")",(int)deleted.StatusCode==409);
        Check("Concurrent order item remains in PostgreSQL",Convert.ToInt64(await Sql("SELECT count(*) FROM \"OrderItems\" WHERE \"Id\"=$1",item))==1);
    }
    // Перевіряємо зовнішній ключ у транзакції без збереження.
    await using(var tx=await db.BeginTransactionAsync()) {
        try { await Sql("INSERT INTO \"Products\" (\"Name\",\"Price\",\"StockQuantity\",\"CategoryId\") VALUES ($1,1,0,2147483647)",tag); Check("PostgreSQL FK rejects missing category",false); }
        catch(PostgresException ex) { Check("PostgreSQL FK rejects missing category",ex.SqlState=="23503"); }
        await tx.RollbackAsync();
    }
}
catch(Exception ex) { Check("Test execution: "+ex.GetType().Name+" "+ex.Message,false); }
finally {
    if(item!=0) await Sql("DELETE FROM \"OrderItems\" WHERE \"Id\"=$1",item);
    if(order!=0) await Sql("DELETE FROM \"Orders\" WHERE \"Id\"=$1",order);
    if(user!=0) await Sql("DELETE FROM \"Users\" WHERE \"Id\"=$1",user);
    foreach(var id in prods) await Sql("DELETE FROM \"Products\" WHERE \"Id\"=$1",id);
    foreach(var id in cats) await Sql("DELETE FROM \"Categories\" WHERE \"Id\"=$1",id);
    foreach(var t in tables) Check("Cleanup row count "+t,Convert.ToInt64(await Sql($"SELECT count(*) FROM \"{t}\""))==initial[t]);
}
report.Insert(0,$"Checks: {report.Count}, failures: {failures}. Run: {tag}");
await File.WriteAllLinesAsync(reportPath,report);
Console.WriteLine(report[0]);
foreach(var line in report.Where(x=>x.StartsWith("FAIL"))) Console.WriteLine(line);
return failures;


}
}

