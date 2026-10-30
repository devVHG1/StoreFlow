using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;
namespace StoreFlow.IntegrationTests;

internal static class ReliabilitySuite
{
    public static async Task<int> RunAsync(string baseUrl,string connection,string token,string reportPath)
    {
        var report=new List<string>(); int failures=0;
        void Check(string label,bool ok){report.Add((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
        using var client=new HttpClient { BaseAddress=new Uri(baseUrl),Timeout=TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        await using var db=new NpgsqlConnection(connection);await db.OpenAsync();
        async Task<object?> Sql(string text,params object[] values){await using var command=new NpgsqlCommand(text,db);foreach(var value in values)command.Parameters.AddWithValue(value);return await command.ExecuteScalarAsync();}
        async Task<JsonNode?> Call(HttpMethod method,string route,object? body,int status)
        {
            using var request=new HttpRequestMessage(method,"/api/"+route);
            if(body is not null)request.Content=JsonContent.Create(body);
            if(method==HttpMethod.Post&&route=="orders")request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
            using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();
            Check(method+" "+route+" returns "+status,(int)response.StatusCode==status);
            if((int)response.StatusCode!=status)throw new Exception(text);
            return text.Length==0?null:JsonNode.Parse(text);
        }
        int Id(JsonNode? node)=>node!["id"]!.GetValue<int>();
        try
        {
            int category=Id(await Call(HttpMethod.Post,"categories",new{name="Reliability QA"},201));
            var product=await Call(HttpMethod.Post,"products",new{name="Початкова назва",price=12.5m,stockQuantity=20,categoryId=category},201);
            int pid=Id(product);
            var order=await Call(HttpMethod.Post,"orders",new{items=new[]{new{productId=pid,quantity=2}}},201);
            int oid=Id(order),item=Id(order!["items"]![0]);
            Check("New order captures original product name",order["items"]![0]!["productName"]!.GetValue<string>()=="Початкова назва"&&!order["items"]![0]!["isNameBackfilled"]!.GetValue<bool>());
            product=await Call(HttpMethod.Get,"products/"+pid,null,200);
            await Call(HttpMethod.Put,"products/"+pid,new{name="Нова назва",price=99,stockQuantity=18,categoryId=category,version=product!["version"]!.GetValue<uint>()},204);
            order=await Call(HttpMethod.Get,"orders/"+oid,null,200);
            Check("Rename does not change historical order name",order!["items"]![0]!["productName"]!.GetValue<string>()=="Початкова назва");
            order=await Call(HttpMethod.Post,$"orders/{oid}/items",new{productId=pid,quantity=1},200);
            Check("Increasing existing position preserves original name and price",order!["items"]![0]!["productName"]!.GetValue<string>()=="Початкова назва"&&order["items"]![0]!["unitPrice"]!.GetValue<decimal>()==12.5m&&order["totalAmount"]!.GetValue<decimal>()==37.5m);
            var latest=await Call(HttpMethod.Post,"orders",new{items=new[]{new{productId=pid,quantity=1}}},201);
            Check("New purchase uses new catalog name and price",latest!["items"]![0]!["productName"]!.GetValue<string>()=="Нова назва"&&latest["items"]![0]!["unitPrice"]!.GetValue<decimal>()==99m);
            await Call(HttpMethod.Put,$"orders/{oid}/items/{item}",new{quantity=2},200);
            var added=Id(await Call(HttpMethod.Post,"products",new{name="Додаткова позиція",price=1,stockQuantity=5,categoryId=category},201));
            order=await Call(HttpMethod.Post,$"orders/{oid}/items",new{productId=added,quantity=1},200);
            Check("Adding another product captures its name",order!["items"]!.AsArray().Any(x=>x!["productName"]!.GetValue<string>()=="Додаткова позиція"));

            foreach(var assignment in new[]{"\"ProductName\"='Змінена історія'","\"UnitPrice\"=2","\"IsNameBackfilled\"=true"})
            {
                try{await Sql($"UPDATE \"OrderItems\" SET {assignment} WHERE \"Id\"=$1",item);Check("Snapshot rejects "+assignment,false);}
                catch(PostgresException error){Check("Snapshot rejects "+assignment,error.SqlState=="23514");}
            }
            int owner=order!["userId"]!.GetValue<int>();
            var count=Convert.ToInt32(await Sql("SELECT count(*) FROM \"Orders\" WHERE NOT \"IsDeleted\" AND \"UserId\"=$1",owner));
            var page=await Call(HttpMethod.Get,$"orders?userId={owner}&pageSize=1",null,200);
            int newest=Convert.ToInt32(await Sql("SELECT max(\"Id\") FROM \"Orders\" WHERE NOT \"IsDeleted\" AND \"UserId\"=$1",owner));
            Check("Order page matches PostgreSQL count and newest id",page!["totalCount"]!.GetValue<int>()==count&&page["items"]!.AsArray().Count==1&&Id(page["items"]![0])==newest&&page["hasNextPage"]!.GetValue<bool>()&&!page["hasPreviousPage"]!.GetValue<bool>());
            var second=await Call(HttpMethod.Get,$"orders?userId={owner}&pageSize=1&page=2",null,200);
            Check("Order pages do not overlap",Id(second!["items"]![0])!=newest&&second["hasPreviousPage"]!.GetValue<bool>()&&second["totalCount"]!.GetValue<int>()==count);
            page=await Call(HttpMethod.Get,$"orders?userId={owner}&pageSize=1&page=2147483647",null,200);
            Check("Out-of-range order page is empty with correct count",page!["items"]!.AsArray().Count==0&&page["totalCount"]!.GetValue<int>()==count&&!page["hasNextPage"]!.GetValue<bool>());
            page=await Call(HttpMethod.Get,$"orders?userId={owner}&status=Pending&pageSize=100",null,200);
            var pendingCount=Convert.ToInt32(await Sql("SELECT count(*) FROM \"Orders\" WHERE NOT \"IsDeleted\" AND \"UserId\"=$1 AND \"Status\"=1",owner));
            Check("Order status filter and count are applied before pagination",page!["totalCount"]!.GetValue<int>()==pendingCount&&page["items"]!.AsArray().All(x=>x!["status"]!.GetValue<string>()=="Pending"&&x["userId"]!.GetValue<int>()==owner));
            foreach(var invalid in new[]{"page=0","page=-1","pageSize=0","pageSize=101","page=2147483647&pageSize=100","status=Unknown","status=1","userId=0","page=abc"})
                await Call(HttpMethod.Get,"orders?"+invalid,null,400);
            page=await Call(HttpMethod.Get,"orders?userId=2147483647",null,200);
            Check("Empty owner filter returns zero page metadata",page!["totalCount"]!.GetValue<int>()==0&&page["totalPages"]!.GetValue<int>()==0&&page["items"]!.AsArray().Count==0&&!page["hasNextPage"]!.GetValue<bool>()&&!page["hasPreviousPage"]!.GetValue<bool>());

            var movements=Convert.ToInt64(await Sql("SELECT count(*) FROM \"StockMovements\" WHERE \"ProductId\"=$1",pid));
            foreach(var assignment in new[]{"\"StockQuantity\"=-1","\"Price\"=0","\"Price\"=-1","\"Price\"='NaN'::numeric","\"Price\"='Infinity'::numeric","\"Price\"='-Infinity'::numeric"})
            {
                try{await Sql($"UPDATE \"Products\" SET {assignment} WHERE \"Id\"=$1",pid);Check("Database rejects "+assignment,false);}
                catch(PostgresException error){Check("Database rejects "+assignment,error.SqlState=="23514"&&error.ConstraintName?.StartsWith("CK_Products_")==true);}
            }
            Check("Rejected writes create no stock movements",movements==Convert.ToInt64(await Sql("SELECT count(*) FROM \"StockMovements\" WHERE \"ProductId\"=$1",pid)));
            foreach(var quantity in new[]{0,-1})
            {
                try{await Sql("INSERT INTO \"OrderItems\" (\"OrderId\",\"ProductId\",\"Quantity\",\"UnitPrice\") VALUES ($1,$2,$3,1)",oid,pid,quantity);Check("Database rejects quantity "+quantity,false);}
                catch(PostgresException error){Check("Database rejects quantity "+quantity,error.SqlState=="23514"&&error.ConstraintName=="CK_OrderItems_QuantityPositive");}
            }
            foreach(var value in new[]{"0","-1","'NaN'::numeric","'Infinity'::numeric"})
            {
                try{await Sql($"INSERT INTO \"OrderItems\" (\"OrderId\",\"ProductId\",\"Quantity\",\"UnitPrice\") VALUES ($1,$2,1,{value})",oid,pid);Check("Database rejects unit price "+value,false);}
                catch(PostgresException error){Check("Database rejects unit price "+value,error.SqlState=="23514"&&error.ConstraintName=="CK_OrderItems_UnitPricePositive");}
            }
            foreach(var value in new[]{"-1","'NaN'::numeric","'Infinity'::numeric"})
            {
                try{await Sql($"UPDATE \"Orders\" SET \"TotalAmount\"={value} WHERE \"Id\"=$1",oid);Check("Database rejects total "+value,false);}
                catch(PostgresException error){Check("Database rejects total "+value,error.SqlState=="23514"&&error.ConstraintName=="CK_Orders_TotalNonnegative");}
            }
        }
        catch(Exception error){Check("Reliability execution: "+error.Message,false);}
        report.Insert(0,$"Reliability: {report.Count} checks; failures: {failures}");
        await File.WriteAllLinesAsync(reportPath,report);Console.WriteLine(report[0]);foreach(var line in report.Where(x=>x.StartsWith("FAIL")))Console.WriteLine(line);
        return failures;
    }
}
