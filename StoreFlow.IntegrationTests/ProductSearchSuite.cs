using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace StoreFlow.IntegrationTests;

internal static class ProductSearchSuite
{
    private record Fixture(int Id, string Name, decimal Price, int StockQuantity, int CategoryId);

    public static async Task<int> RunAsync(string baseUrl, string connection, string token, string reportPath)
    {
        var checks = new List<string>(); int failures = 0;
        void Check(string label, bool ok) { checks.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
        using var client = new HttpClient(new ContractHandler()) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token);
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        async Task<object?> Sql(string sql, params object[] values)
        {
            await using var cmd = new NpgsqlCommand(sql,db);
            foreach(var value in values) cmd.Parameters.AddWithValue(value);
            return await cmd.ExecuteScalarAsync();
        }
        async Task<JsonNode> Request(string method,string path,object? body,int expected,string? field=null)
        {
            using var request=new HttpRequestMessage(new HttpMethod(method),path);
            if(body!=null) request.Content=JsonContent.Create(body);
            using var response=await client.SendAsync(request);
            var text=await response.Content.ReadAsStringAsync();
            Check(method+" "+path+" => "+expected,(int)response.StatusCode==expected);
            if((int)response.StatusCode!=expected) throw new Exception("Unexpected search response "+(int)response.StatusCode);
            var result=JsonNode.Parse(text)!;
            if(field!=null) Check("Validation field "+field,result["errors"]?[field] is JsonArray {Count:>0});
            return result;
        }
        string Query(string value)=>Uri.EscapeDataString(value);
        JsonArray Items(JsonNode node)=>node["items"]!.AsArray();
        int[] Ids(JsonNode node)=>Items(node).Select(x=>x!["id"]!.GetValue<int>()).ToArray();
        var fixtures=new List<Fixture>(); var categories=new List<int>();
        var tag="Search-"+Guid.NewGuid().ToString("N");
        long productsBefore=Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\""));
        long categoriesBefore=Convert.ToInt64(await Sql("SELECT count(*) FROM \"Categories\""));
        try
        {
            foreach(var suffix in new[]{" A"," B"})
                categories.Add((await Request("POST","/api/categories",new{name=tag+suffix},201))["id"]!.GetValue<int>());
            async Task Add(string name,decimal price,int stock,int categoryId)
            {
                var node=await Request("POST","/api/products",new{name,price,stockQuantity=stock,categoryId},201);
                fixtures.Add(new(node["id"]!.GetValue<int>(),name,price,stock,categoryId));
            }
            for(int i=0;i<27;i++) await Add(tag+"-Item-"+i.ToString("D2"),(i%5+1)*10m,i,categories[i<15?0:1]);
            foreach(var suffix in new[]{" Literal%percent"," Literal_under"," Literal\\slash"," Український товар"})
                await Add(tag+suffix,12.50m,0,categories[0]);
            Check("Fixtures exist in PostgreSQL",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\" WHERE \"Id\"=ANY($1)",fixtures.Select(x=>x.Id).ToArray()))==31);

            var page=await Request("GET","/api/products",null,200);
            Check("Default page is bounded to 20 items",page["page"]!.GetValue<int>()==1 && page["pageSize"]!.GetValue<int>()==20 && Items(page).Count==20);
            Check("Default total equals PostgreSQL count",page["totalCount"]!.GetValue<int>()==Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\"")));
            page=await Request("GET","/api/products?search="+Query(tag),null,200);
            Check("Search total, total pages and navigation flags",page["totalCount"]!.GetValue<int>()==31 && page["totalPages"]!.GetValue<int>()==2 &&
                page["hasNextPage"]!.GetValue<bool>() && !page["hasPreviousPage"]!.GetValue<bool>());
            page=await Request("GET","/api/products?search="+Query("  "+tag.ToUpperInvariant()+"  ")+"&pageSize=100",null,200);
            Check("Search trims input and ignores case",Ids(page).ToHashSet().SetEquals(fixtures.Select(x=>x.Id)));
            page=await Request("GET","/api/products?search="+Query(tag+" УКРАЇНСЬКИЙ"),null,200);
            Check("Ukrainian search ignores case",Items(page).Count==1 && Items(page)[0]!["name"]!.GetValue<string>().EndsWith("Український товар"));
            foreach(var literal in new[]{" Literal%"," Literal_"," Literal\\"})
            {
                page=await Request("GET","/api/products?search="+Query(tag+literal),null,200);
                Check("LIKE metacharacter is literal: "+literal,page["totalCount"]!.GetValue<int>()==1);
            }
            page=await Request("GET","/api/products?search="+Query("' OR 1=1 --"),null,200);
            Check("SQL-like search text is treated as data",page["totalCount"]!.GetValue<int>()==0);
            page=await Request("GET","/api/products?search=%20%20&pageSize=100",null,200);
            Check("Whitespace search applies no filter",page["totalCount"]!.GetValue<int>()==Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\"")));

            page=await Request("GET",$"/api/products?categoryId={categories[0]}&pageSize=100",null,200);
            Check("Category filter returns exactly its products",Ids(page).ToHashSet().SetEquals(fixtures.Where(x=>x.CategoryId==categories[0]).Select(x=>x.Id)));
            page=await Request("GET","/api/products?search="+Query(tag)+"&minPrice=20&maxPrice=40&pageSize=100",null,200);
            Check("Price boundaries are inclusive",Ids(page).ToHashSet().SetEquals(fixtures.Where(x=>x.Price>=20m && x.Price<=40m).Select(x=>x.Id)));
            page=await Request("GET","/api/products?search="+Query(tag)+"&minPrice=12.50&maxPrice=12.50&pageSize=100",null,200);
            Check("Decimal exact-price filter",Ids(page).ToHashSet().SetEquals(fixtures.Where(x=>x.Price==12.50m).Select(x=>x.Id)));
            page=await Request("GET","/api/products?search="+Query(tag)+"&minPrice=40&pageSize=100",null,200);
            Check("Min-only price filter",Ids(page).ToHashSet().SetEquals(fixtures.Where(x=>x.Price>=40m).Select(x=>x.Id)));
            page=await Request("GET","/api/products?search="+Query(tag)+"&maxPrice=20&pageSize=100",null,200);
            Check("Max-only price filter",Ids(page).ToHashSet().SetEquals(fixtures.Where(x=>x.Price<=20m).Select(x=>x.Id)));
            page=await Request("GET","/api/products?maxPrice=0",null,200);
            Check("Zero price filter is valid and empty",page["totalCount"]!.GetValue<int>()==0);

            var regular=fixtures.Where(x=>x.Name.Contains("-Item-")).ToList();
            foreach(var sortBy in new[]{"id","name","price","stockQuantity"})
            foreach(var direction in new[]{"asc","desc"})
            {
                bool desc=direction=="desc";
                IOrderedEnumerable<Fixture> expected=sortBy switch
                {
                    "name"=>desc?regular.OrderByDescending(x=>x.Name,StringComparer.Ordinal):regular.OrderBy(x=>x.Name,StringComparer.Ordinal),
                    "price"=>desc?regular.OrderByDescending(x=>x.Price):regular.OrderBy(x=>x.Price),
                    "stockQuantity"=>desc?regular.OrderByDescending(x=>x.StockQuantity):regular.OrderBy(x=>x.StockQuantity),
                    _=>desc?regular.OrderByDescending(x=>x.Id):regular.OrderBy(x=>x.Id)
                };
                if(sortBy!="id") expected=expected.ThenBy(x=>x.Id);
                page=await Request("GET","/api/products?search="+Query(tag+"-Item-")+"&sortBy="+sortBy+"&sortDirection="+direction+"&pageSize=100",null,200);
                Check("Stable ordering "+sortBy+" "+direction,Ids(page).SequenceEqual(expected.Select(x=>x.Id)));
            }
            var selected=fixtures.Where(x=>x.CategoryId==categories[0] && x.Price>=20m && x.Price<=40m).OrderByDescending(x=>x.Price).ThenBy(x=>x.Id).ToList();
            page=await Request("GET","/api/products?search="+Query(tag)+$"&categoryId={categories[0]}&minPrice=20&maxPrice=40&sortBy=price&sortDirection=desc&page=2&pageSize=2",null,200);
            Check("Combined filters, sorting and pagination",Ids(page).SequenceEqual(selected.Skip(2).Take(2).Select(x=>x.Id)) && page["totalCount"]!.GetValue<int>()==selected.Count);

            var collected=new List<int>();
            for(int number=1;number<=5;number++)
            {
                page=await Request("GET","/api/products?search="+Query(tag)+"&pageSize=7&page="+number,null,200);
                collected.AddRange(Ids(page));
                Check("Page metadata "+number,page["page"]!.GetValue<int>()==number && page["totalCount"]!.GetValue<int>()==31 && page["totalPages"]!.GetValue<int>()==5);
            }
            Check("Pagination has no gaps or duplicate IDs",collected.Count==31 && collected.Distinct().Count()==31 && collected.SequenceEqual(fixtures.Select(x=>x.Id).Order()));
            Check("Last page navigation",!page["hasNextPage"]!.GetValue<bool>() && page["hasPreviousPage"]!.GetValue<bool>());
            page=await Request("GET","/api/products?search="+Query(tag)+"&pageSize=7&page=6",null,200);
            Check("Out-of-range page is empty with accurate total",Items(page).Count==0 && page["totalCount"]!.GetValue<int>()==31);
            page=await Request("GET","/api/products?categoryId=2147483647",null,200);
            Check("Unknown category filter is empty",Items(page).Count==0 && page["totalCount"]!.GetValue<int>()==0 && page["totalPages"]!.GetValue<int>()==0);
            foreach(var (query,field) in new[]{
                ("page=0","page"),("page=-1","page"),("page=abc","page"),("page=2147483647&pageSize=100","page"),
                ("pageSize=0","pageSize"),("pageSize=-1","pageSize"),("pageSize=101","pageSize"),("pageSize=abc","pageSize"),
                ("categoryId=0","categoryId"),("categoryId=-1","categoryId"),("categoryId=abc","categoryId"),
                ("minPrice=-0.01","minPrice"),("maxPrice=-1","maxPrice"),("minPrice=abc","minPrice"),
                ("minPrice=30&maxPrice=20","maxPrice"),("sortBy=description","sortBy"),("sortBy=price%3BDROP%20TABLE","sortBy"),
                ("sortBy=","sortBy"),("sortDirection=sideways","sortDirection"),("search="+new string('a',201),"search")
            }) await Request("GET","/api/products?"+query,null,400,field);
        }
        catch(Exception error) { Check("Search test execution: "+error.GetType().Name+" "+error.Message,false); }
        finally
        {
            foreach(var fixture in fixtures) await Sql("DELETE FROM \"Products\" WHERE \"Id\"=$1",fixture.Id);
            foreach(var category in categories) await Sql("DELETE FROM \"Categories\" WHERE \"Id\"=$1",category);
            Check("Search cleanup preserves product count",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Products\""))==productsBefore);
            Check("Search cleanup preserves category count",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Categories\""))==categoriesBefore);
        }
        checks.Insert(0,$"Search: {checks.Count} checks; failures: {failures}");
        await File.WriteAllLinesAsync(reportPath,checks);
        Console.WriteLine(checks[0]);
        foreach(var line in checks.Where(x=>x.StartsWith("FAIL"))) Console.WriteLine(line);
        return failures;
    }
}
