using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace StoreFlow.IntegrationTests;

internal static class AuthSuite
{
    public static async Task<int> RunAsync(string root, string baseUrl, string connection, string jwtKey,
        string adminToken, string adminEmail, string adminPassword, string reportPath)
    {
        var checks = new List<string>();
        int failures = 0;
        void Check(string label, bool ok) { checks.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
        using var client = new HttpClient(new ContractHandler()) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        async Task<object?> Sql(string sql, params object[] values)
        {
            await using var cmd = new NpgsqlCommand(sql, db);
            foreach(var value in values) cmd.Parameters.AddWithValue(value);
            return await cmd.ExecuteScalarAsync();
        }
        async Task<JsonNode?> Call(string method, string path, object? body, int expected, string? token = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body != null) request.Content = JsonContent.Create(body);
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Check(method + " " + path + " => " + expected, (int)response.StatusCode == expected);
            if ((int)response.StatusCode != expected) throw new Exception("Unexpected auth test HTTP status " + (int)response.StatusCode);
            if (expected >= 400)
                Check("ProblemDetails does not reveal credentials", JsonNode.Parse(text)?["status"]?.GetValue<int>() == expected &&
                    !text.Contains("PasswordHash") && !text.Contains("StackTrace") && !text.Contains(adminPassword));
            if(expected == 401) Check("401 has Bearer challenge", response.Headers.WwwAuthenticate.Any(x=>x.Scheme=="Bearer"));
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
        static string Token(JsonNode? node) => node!["accessToken"]!.GetValue<string>();
        static int Id(JsonNode? node) => node!["user"]!["id"]!.GetValue<int>();
        const string password = "StoreFlow-Test#Customer-2026!";
        const string newPassword = "StoreFlow-New#Password-2026!";
        object Register(string email, string pass = password) => new { firstName = "  Тест  ", lastName = "Клієнт", email, password = pass, role = "Admin", tokenVersion = 999 };
        async Task<JsonNode?> Login(string email, string pass = password, int status = 200) =>
            await Call("POST","/api/auth/login",new { email, password = pass }, status);
        try
        {
            await Call("GET","/api/categories",null,200);
            await Call("GET","/api/products",null,200);
            await Call("GET","/api/orders",null,401);
            await Call("POST","/api/orders",new{items=new[]{new{productId=1,quantity=1}}},401);
            await Call("POST","/api/categories",new{name="Anonymous"},401);
            await Call("GET","/api/users/me",null,401);
            await Call("GET","/api/users",null,401);
            await Call("POST","/api/auth/logout",null,401);
            await Call("POST","/api/auth/register",Register("bad-email"),400);
            await Call("POST","/api/auth/register",Register("short@example.invalid","short"),400);
            await Call("POST","/api/auth/register",new{firstName=" ",lastName="QA",email="blank@example.invalid",password},400);
            await Call("POST","/api/auth/register",new{},400);
            await Call("POST","/api/auth/login",new{},400);

            var first=await Call("POST","/api/auth/register",Register("First@Auth.Example.Invalid"),201);
            string firstToken=Token(first);
            int firstId=Id(first);
            Check("Registration cannot choose Admin or token version",first!["user"]!["role"]!.GetValue<string>()=="Customer" &&
                Convert.ToInt32(await Sql("SELECT \"TokenVersion\" FROM \"Users\" WHERE \"Id\"=$1",firstId))==0);
            Check("Registration trims profile names",first["user"]!["firstName"]!.GetValue<string>()=="Тест");
            Check("JWT lifetime is 15 minutes",Math.Abs((DateTime.Parse(first["expiresAtUtc"]!.GetValue<string>()).ToUniversalTime()-DateTime.UtcNow).TotalMinutes-15)<0.1);
            Check("Passwords are hashed", (string?)await Sql("SELECT \"PasswordHash\" FROM \"Users\" WHERE \"Id\"=$1",firstId) != password);
            Check("Normalized email persisted", (string?)await Sql("SELECT \"NormalizedEmail\" FROM \"Users\" WHERE \"Id\"=$1",firstId)=="first@auth.example.invalid");
            await Call("POST","/api/auth/register",Register("FIRST@AUTH.EXAMPLE.INVALID"),409);
            var firstAgain=await Login("FIRST@AUTH.EXAMPLE.INVALID");
            var second=await Call("POST","/api/auth/register",Register("second@auth.example.invalid"),201);
            string secondToken=Token(second); int secondId=Id(second);
            Check("Same passwords have distinct salted hashes",!Equals(await Sql("SELECT \"PasswordHash\" FROM \"Users\" WHERE \"Id\"=$1",firstId),await Sql("SELECT \"PasswordHash\" FROM \"Users\" WHERE \"Id\"=$1",secondId)));

            var me=await Call("GET","/api/users/me",null,200,firstToken);
            Check("User DTO excludes private fields",me!["passwordHash"]==null && me["normalizedEmail"]==null && me["tokenVersion"]==null && me["failedLoginAttempts"]==null);
            me=await Call("PUT","/api/users/me",new{firstName="  Нове  ",lastName="Ім’я",role="Admin",email=adminEmail},200,firstToken);
            Check("Profile update cannot change email or role",me!["firstName"]!.GetValue<string>()=="Нове" && me["role"]!.GetValue<string>()=="Customer" && me["email"]!.GetValue<string>()=="First@Auth.Example.Invalid");
            await Call("GET","/api/users",null,403,firstToken);
            await Call("GET","/api/users/"+secondId,null,403,firstToken);
            await Call("PUT","/api/users/"+firstId+"/role",new{role="Admin"},403,firstToken);
            await Call("POST","/api/categories",new{name="Forbidden"},403,firstToken);

            var staff=await Call("POST","/api/auth/register",Register("manager@auth.example.invalid"),201);
            int staffId=Id(staff);
            string oldStaffToken=Token(staff);
            await Call("PUT","/api/users/"+staffId+"/role",new{role="Manager"},200,adminToken);
            await Call("GET","/api/users/me",null,401,oldStaffToken);
            string managerToken=Token(await Login("manager@auth.example.invalid"));
            await Call("GET","/api/users",null,403,managerToken);
            await Call("PUT","/api/users/"+staffId+"/role",new{role="Admin"},403,managerToken);
            await Call("PUT","/api/users/"+staffId+"/role",new{role="Unknown"},400,adminToken);
            await Call("PUT","/api/users/2147483647/role",new{role="Customer"},404,adminToken);
            var users=await Call("GET","/api/users",null,200,adminToken);
            Check("Admin user list has no password hashes",users!.AsArray().All(x=>x!["passwordHash"]==null && x["tokenVersion"]==null));
            int adminId=users.AsArray().Single(x=>x!["email"]!.GetValue<string>()==adminEmail)!["id"]!.GetValue<int>();
            await Call("GET","/api/users/2147483647",null,404,adminToken);
            await Call("PUT","/api/users/"+adminId+"/role",new{role="Customer"},409,adminToken);
            await Call("PUT","/api/users/"+adminId+"/role",new{role="Admin"},200,adminToken);
            await Call("GET","/api/users/me",null,200,adminToken);

            int category=(await Call("POST","/api/categories",new{name="Auth category"},201,managerToken))!["id"]!.GetValue<int>();
            int product=(await Call("POST","/api/products",new{name="Auth product",price=3.5m,stockQuantity=10,categoryId=category},201,managerToken))!["id"]!.GetValue<int>();
            await Call("PUT","/api/products/"+product,new{name="Forbidden",price=1,stockQuantity=1,categoryId=category},403,firstToken);
            await Call("DELETE","/api/products/"+product,null,403,firstToken);
            await Call("DELETE","/api/categories/"+category,null,403,firstToken);
            var own=await Call("POST","/api/orders",new{items=new[]{new{productId=product,quantity=2}}},201,firstToken);
            int order=own!["id"]!.GetValue<int>(), item=own["items"]![0]!["id"]!.GetValue<int>();
            Check("Customer order owner comes from JWT",own["userId"]!.GetValue<int>()==firstId);
            await Call("POST","/api/orders",new{userId=secondId,items=new[]{new{productId=product,quantity=1}}},403,firstToken);
            await Call("GET","/api/orders/"+order,null,404,secondToken);
            await Call("POST",$"/api/orders/{order}/items",new{productId=product,quantity=1},404,secondToken);
            await Call("PUT",$"/api/orders/{order}/items/{item}",new{quantity=1},404,secondToken);
            await Call("DELETE",$"/api/orders/{order}/items/{item}",null,404,secondToken);
            await Call("PUT",$"/api/orders/{order}/status",new{status="Cancelled"},404,secondToken);
            await Call("DELETE","/api/orders/"+order,null,404,secondToken);
            var orders=await Call("GET","/api/orders",null,200,secondToken);
            Check("Customer list excludes other customers' orders",orders!.AsArray().All(x=>x!["userId"]!.GetValue<int>()==secondId) && orders.AsArray().All(x=>x!["id"]!.GetValue<int>()!=order));
            orders=await Call("GET","/api/orders",null,200,firstToken);
            Check("Customer list includes own order",orders!.AsArray().Any(x=>x!["id"]!.GetValue<int>()==order) && orders.AsArray().All(x=>x!["userId"]!.GetValue<int>()==firstId));
            await Call("GET","/api/orders/"+order,null,200,managerToken);
            await Call("PUT",$"/api/orders/{order}/status",new{status="Processing"},403,firstToken);
            await Call("PUT",$"/api/orders/{order}/status",new{status="Cancelled"},200,firstToken);
            Check("Own cancellation restores stock",Convert.ToInt32(await Sql("SELECT \"StockQuantity\" FROM \"Products\" WHERE \"Id\"=$1",product))==10);
            await Call("DELETE","/api/orders/"+order,null,204,firstToken);

            own=await Call("POST","/api/orders",new{userId=secondId,items=new[]{new{productId=product,quantity=1}}},201,managerToken);
            order=own!["id"]!.GetValue<int>();
            Check("Manager may create order for existing customer",own["userId"]!.GetValue<int>()==secondId);
            await Call("PUT",$"/api/orders/{order}/status",new{status="Processing"},200,managerToken);
            await Call("PUT",$"/api/orders/{order}/status",new{status="Cancelled"},200,secondToken);
            await Call("DELETE","/api/orders/"+order,null,204,managerToken);

            // Перевіряємо відкликання всіх токенів після зміни пароля.
            await Call("PUT","/api/users/me/password",new{currentPassword="wrong-password",newPassword},400,firstToken);
            await Call("GET","/api/users/me",null,200,firstToken);
            await Call("PUT","/api/users/me/password",new{currentPassword=password,newPassword="short"},400,firstToken);
            await Call("PUT","/api/users/me/password",new{currentPassword=password,newPassword},204,firstToken);
            await Call("GET","/api/users/me",null,401,firstToken);
            await Call("GET","/api/users/me",null,401,Token(firstAgain));
            await Login("first@auth.example.invalid",password,401);
            string changedToken=Token(await Login("first@auth.example.invalid",newPassword));
            string changedToken2=Token(await Login("first@auth.example.invalid",newPassword));
            await Call("POST","/api/auth/logout",null,204,changedToken);
            await Call("GET","/api/users/me",null,401,changedToken);
            await Call("GET","/api/users/me",null,401,changedToken2);
            await Call("POST","/api/auth/logout",null,401,changedToken);

            var locked=await Call("POST","/api/auth/register",Register("locked@auth.example.invalid"),201);
            int lockedId=Id(locked);
            JsonNode? wrong=null, unknown=null;
            for(int attempt=0;attempt<5;attempt++) wrong=await Login("locked@auth.example.invalid","wrong-password",401);
            unknown=await Login("missing@auth.example.invalid","wrong-password",401);
            Check("Login errors do not reveal account existence",wrong!["title"]!.GetValue<string>()==unknown!["title"]!.GetValue<string>());
            await Login("locked@auth.example.invalid",password,401);
            Check("Five failed logins persist lockout",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Users\" WHERE \"Id\"=$1 AND \"LockoutEndUtc\">now()",lockedId))==1);
            await Sql("UPDATE \"Users\" SET \"LockoutEndUtc\"=now()-interval '1 minute' WHERE \"Id\"=$1",lockedId);
            await Login("locked@auth.example.invalid");
            Check("Successful login clears lockout state",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Users\" WHERE \"Id\"=$1 AND \"LockoutEndUtc\" IS NULL AND \"FailedLoginAttempts\"=0",lockedId))==1);

            var duplicateRequests=await Task.WhenAll(
                client.PostAsJsonAsync("/api/auth/register",Register("parallel@auth.example.invalid")),
                client.PostAsJsonAsync("/api/auth/register",Register("PARALLEL@AUTH.EXAMPLE.INVALID")));
            Check("Concurrent duplicate registration: one 201 and one 409",duplicateRequests.Count(x=>(int)x.StatusCode==201)==1 && duplicateRequests.Count(x=>(int)x.StatusCode==409)==1);
            foreach(var response in duplicateRequests) response.Dispose();
            Check("PostgreSQL enforces normalized email uniqueness",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Users\" WHERE \"NormalizedEmail\"='parallel@auth.example.invalid'"))==1);

            // Навмисно створюємо недійсні JWT, щоб перевірити всі параметри валідації.
            static string B64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
            string Forged(string key, string issuer="StoreFlow",string audience="StoreFlow.Client",long? expires=null,long? starts=null,string? role=null,string? subject=null,int version=0,bool unsigned=false)
            {
                var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var header=B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{alg=unsigned?"none":"HS256",typ="JWT"})));
                var payload=B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string,object>{
                    ["iss"]=issuer,["aud"]=audience,["sub"]=subject??secondId.ToString(),["role"]=role??"Customer",["ver"]=version.ToString(),
                    ["iat"]=now,["nbf"]=starts??now-60,["exp"]=expires??now+600,["jti"]=Guid.NewGuid().ToString("N") })));
                var unsignedText=header+"."+payload;
                return unsignedText+"."+(unsigned?"":B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),Encoding.UTF8.GetBytes(unsignedText))));
            }
            foreach(var invalidToken in new[]{
                "not-a-token",Forged(new string('x',64)),Forged(jwtKey,issuer:"Wrong"),Forged(jwtKey,audience:"Wrong"),
                Forged(jwtKey,expires:DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()),
                Forged(jwtKey,starts:DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()),
                Forged(jwtKey,role:"Admin"),Forged(jwtKey,subject:"2147483647"),Forged(jwtKey,subject:"not-an-id"),
                Forged(jwtKey,version:99),Forged(jwtKey,unsigned:true)
            }) await Call("GET","/api/users/me",null,401,invalidToken);
            await Call("GET","/api/users/me",null,200,secondToken);

            await Call("PUT","/api/users/"+staffId+"/role",new{role="Customer"},200,adminToken);
            await Call("POST","/api/categories",new{name="Stale manager"},401,managerToken);
            string downgraded=Token(await Login("manager@auth.example.invalid"));
            await Call("POST","/api/categories",new{name="No longer manager"},403,downgraded);

            // Окремий процес перевіряє справжній ліміт запитів.
            var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
            int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"StoreFlow.Аpi"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(TestPaths.ApiDll(root));
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add("http://localhost:"+port);
            start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
            start.Environment["ConnectionStrings__DefaultConnection"]=connection;
            start.Environment["Jwt__Key"]=jwtKey;
            start.Environment["Auth__RequestsPerMinute"]="2";
            using(var rateApi=Process.Start(start)!)
            {
                rateApi.BeginOutputReadLine(); rateApi.BeginErrorReadLine();
                try
                {
                    using var limited=new HttpClient{BaseAddress=new Uri("http://localhost:"+port),Timeout=TimeSpan.FromSeconds(10)};
                    bool ready=false;
                    for(int attempt=0;attempt<50;attempt++)
                    {
                        try { using var response=await limited.GetAsync("/api/products"); if(response.IsSuccessStatusCode){ready=true;break;} } catch(HttpRequestException){}
                        await Task.Delay(100);
                    }
                    Check("Rate-limited API starts",ready);
                    for(int attempt=0;attempt<3;attempt++)
                    {
                        using var response=await limited.PostAsJsonAsync("/api/auth/login",new{email="unknown@example.invalid",password});
                        Check("Auth rate limit request "+(attempt+1),(int)response.StatusCode==(attempt<2?401:429));
                        if(attempt==2) Check("429 includes Retry-After",response.Headers.RetryAfter!=null);
                    }
                }
                finally { if(!rateApi.HasExited) { rateApi.Kill(true); await rateApi.WaitForExitAsync(); } }
            }

            // Повторний локальний bootstrap не створює ще одного адміністратора.
            start.ArgumentList.RemoveAt(start.ArgumentList.Count-1); start.ArgumentList.RemoveAt(start.ArgumentList.Count-1);
            start.ArgumentList.Add("--bootstrap-admin");
            start.Environment["BootstrapAdmin__Email"]="second-admin-bootstrap@example.invalid";
            start.Environment["BootstrapAdmin__Password"]=adminPassword;
            using(var bootstrap=Process.Start(start)!)
            {
                var stdout=bootstrap.StandardOutput.ReadToEndAsync(); var stderr=bootstrap.StandardError.ReadToEndAsync();
                await bootstrap.WaitForExitAsync();
                Check("Bootstrap refuses when an Admin exists",bootstrap.ExitCode!=0);
                Check("Bootstrap error does not reveal password",!(await stdout).Contains(adminPassword) && !(await stderr).Contains(adminPassword));
            }

            await Call("PUT","/api/users/"+secondId+"/role",new{role="Admin"},200,adminToken);
            string secondAdmin=Token(await Login("second@auth.example.invalid"));
            async Task<int> Demote(int id,string token)
            {
                using var request=new HttpRequestMessage(HttpMethod.Put,"/api/users/"+id+"/role"){Content=JsonContent.Create(new{role="Customer"})};
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                using var response=await client.SendAsync(request);
                return (int)response.StatusCode;
            }
            var demotions=await Task.WhenAll(Demote(adminId,adminToken),Demote(secondId,secondAdmin));
            Check("Concurrent demotion preserves last Admin",demotions.Count(x=>x==200)==1 && demotions.Count(x=>x==409)==1);
            Check("Exactly one Admin remains",Convert.ToInt64(await Sql("SELECT count(*) FROM \"Users\" WHERE \"Role\"='Admin'"))==1);
        }
        catch(Exception error) { Check("Auth test execution: "+error.GetType().Name+" "+error.Message,false); }
        checks.Insert(0,$"Auth: {checks.Count} checks; failures: {failures}");
        await File.WriteAllLinesAsync(reportPath,checks);
        Console.WriteLine(checks[0]);
        foreach(var line in checks.Where(x=>x.StartsWith("FAIL"))) Console.WriteLine(line);
        return failures;
    }
}

