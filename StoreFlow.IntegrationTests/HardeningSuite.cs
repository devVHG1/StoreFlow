using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreFlow.Application.Interfaces;
using StoreFlow.Application.Services;
using StoreFlow.Infrastructure.Persistence;

namespace StoreFlow.IntegrationTests;

internal static class HardeningSuite
{
    public static async Task<int> RunAsync(string baseUrl, string connection, string token, string reportPath)
    {
        var report = new List<string>();
        int failures = 0;
        void Check(string label, bool ok) { lock (report) { report.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; } }
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        async Task<object?> Sql(string sql, params object[] values)
        {
            await using var command = new NpgsqlCommand(sql, db);
            foreach (var value in values) command.Parameters.AddWithValue(value);
            return await command.ExecuteScalarAsync();
        }
        async Task<JsonNode?> Call(string method, string path, object? body, int expected, string? key = null, string? accessToken = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Check($"{method} {path}: expected {expected}, actual {(int)response.StatusCode}", (int)response.StatusCode == expected);
            if ((int)response.StatusCode != expected) throw new Exception(text);
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
        int Id(JsonNode? node) => node!["id"]!.GetValue<int>();
        object Body(int product, int quantity) => new { items = new[] { new { productId = product, quantity } } };
        async Task<int> Stock(int product) => Convert.ToInt32(await Sql("SELECT \"StockQuantity\" FROM \"Products\" WHERE \"Id\"=$1", product));
        async Task<long> Movements(int product) => Convert.ToInt64(await Sql("SELECT count(*) FROM \"StockMovements\" WHERE \"ProductId\"=$1", product));
        async Task Expire()
        {
            await using var context = new StoreFlowDbContext(new DbContextOptionsBuilder<StoreFlowDbContext>().UseNpgsql(connection).Options);
            await new OrderService(new OrderRepository(context), new SystemUser()).ExpireReservationsAsync(CancellationToken.None);
        }
        try
        {
            var category = Id(await Call("POST", "/api/categories", new { name = "Hardening QA" }, 201));
            var product = await Call("POST", "/api/products", new { name = "Reserved product", price = 12.5m, stockQuantity = 10, categoryId = category }, 201);
            int pid = Id(product);
            uint originalVersion = product!["version"]!.GetValue<uint>();
            Check("Product exposes database concurrency version", originalVersion > 0);
            Check("Initial balance is recorded", await Movements(pid) == 1);
            var balanceActor = await Sql("SELECT \"ActorId\" FROM \"StockMovements\" WHERE \"ProductId\"=$1", pid);
            Check("Initial movement records authenticated actor", balanceActor is int actor && actor > 0);
            await Call("PUT", $"/api/products/{pid}", new { name = "No version", price = 12.5m, stockQuantity = 10, categoryId = category }, 400);
            await Call("POST", "/api/orders", Body(pid, 2), 400);
            await Call("POST", "/api/orders", Body(pid, 2), 400, "invalid key");
            var key = Guid.NewGuid().ToString("N");
            var order = await Call("POST", "/api/orders", Body(pid, 2), 201, key);
            int oid = Id(order);
            Check("Creation reserves stock once", await Stock(pid) == 8);
            Check("Pending reservation has 30-minute deadline", order!["reservationExpiresAtUtc"]!.GetValue<DateTime>() > DateTime.UtcNow.AddMinutes(29));
            var retries = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call("POST", "/api/orders", Body(pid, 2), 201, key)));
            Check("Concurrent retries return same order", retries.All(x => Id(x) == oid));
            Check("Concurrent retries do not reserve again", await Stock(pid) == 8 && await Movements(pid) == 2);
            Check("Only one idempotency record exists", Convert.ToInt64(await Sql("SELECT count(*) FROM \"OrderRequests\" WHERE \"Key\"=$1", key)) == 1);
            await Call("POST", "/api/orders", Body(pid, 3), 409, key);
            Check("Changed payload cannot reuse key", await Stock(pid) == 8);
            var history = await Call("GET", $"/api/orders/{oid}/history", null, 200);
            Check("Creation history contains initial state", history!.AsArray().Count == 1 && history[0]!["previousStatus"] is null && history[0]!["status"]!.GetValue<string>() == "Pending");

            await Call("PUT", $"/api/products/{pid}", new { name = "Stale editor", price = 13, stockQuantity = 10, categoryId = category, version = originalVersion }, 409);
            Check("Old editor cannot restore reserved stock", await Stock(pid) == 8);
            product = await Call("GET", $"/api/products/{pid}", null, 200);
            var freshVersion = product!["version"]!.GetValue<uint>();
            await Call("PUT", $"/api/products/{pid}", new { name = "Fresh editor", price = 13, stockQuantity = 8, categoryId = category, version = freshVersion }, 204);
            Check("Metadata update does not invent stock movement", await Movements(pid) == 2);

            // Змушуємо PUT чекати блокування, потім змінюємо залишок у конкурентній транзакції.
            product = await Call("GET", $"/api/products/{pid}", null, 200);
            await using (var transaction = await db.BeginTransactionAsync())
            {
                await Sql("SELECT \"Id\" FROM \"Products\" WHERE \"Id\"=$1 FOR UPDATE", pid);
                var update = Call("PUT", $"/api/products/{pid}", new { name = "Concurrent editor", price = 13, stockChangeReason = "QA correction", stockQuantity = 9, categoryId = category, version = product!["version"]!.GetValue<uint>() }, 409);
                bool waiting = false;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    await Sql("SELECT pg_stat_clear_snapshot()");
                    waiting = Convert.ToBoolean(await Sql("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND " + db.ProcessID + " = ANY(pg_blocking_pids(pid)))"));
                    if (waiting) break;
                    await Task.Delay(50);
                }
                Check("Concurrent PUT reached database lock", waiting);
                await Sql("UPDATE \"Products\" SET \"StockQuantity\"=7 WHERE \"Id\"=$1", pid);
                await transaction.CommitAsync();
                await update;
            }
            Check("Database version rejects lost update after waiting", await Stock(pid) == 7);

            var customer = await Call("POST", "/api/auth/register", new { firstName = "Audit", lastName = "Customer", email = "hardening-" + Guid.NewGuid().ToString("N") + "@example.invalid", password = "Hardening-Test#2026!" }, 201);
            var customerToken = customer!["accessToken"]!.GetValue<string>();
            await Call("GET", $"/api/orders/{oid}/history", null, 404, accessToken: customerToken);
            await Call("GET", "/api/stock-movements", null, 403, accessToken: customerToken);
            var ownOrder = await Call("POST", "/api/orders", Body(pid, 1), 201, key, customerToken);
            Check("Idempotency key is scoped to authenticated actor", Id(ownOrder) != oid && await Stock(pid) == 6);
            await Call("GET", $"/api/orders/{Id(ownOrder)}/history", null, 200, accessToken: customerToken);

            await Call("PUT", $"/api/orders/{oid}/status", new { status = "Processing", reason = "Підтверджено менеджером" }, 200);
            order = await Call("GET", $"/api/orders/{oid}", null, 200);
            Check("Processing ends pending reservation deadline", order!["reservationExpiresAtUtc"] is null);
            await Call("PUT", $"/api/orders/{oid}/status", new { status = "Cancelled" }, 200);
            var movementCount = await Movements(pid);
            await Call("PUT", $"/api/orders/{oid}/status", new { status = "Cancelled" }, 200);
            Check("Repeated cancellation creates no extra movement", await Movements(pid) == movementCount && await Stock(pid) == 8);
            history = await Call("GET", $"/api/orders/{oid}/history", null, 200);
            Check("Status history preserves all transitions without duplicate", history!.AsArray().Count == 3 && history[1]!["previousStatus"]!.GetValue<string>() == "Pending" && history[2]!["status"]!.GetValue<string>() == "Cancelled");
            Check("Status history retains operator reason", history![1]!["reason"]!.GetValue<string>().Contains("Підтверджено менеджером"));
            Check("Status history includes actor and time", history.AsArray().All(x => x!["actorId"] is not null && x["occurredAtUtc"] is not null));
            var replay = await Call("POST", "/api/orders", Body(pid, 2), 201, key);
            Check("Retry after cancellation does not recreate reservation", Id(replay) == oid && await Stock(pid) == 8);

            var expiring = Id(ownOrder);
            await Sql("UPDATE \"Orders\" SET \"ReservationExpiresAtUtc\"=now()-interval '1 second' WHERE \"Id\"=$1", expiring);
            await Call("PUT", $"/api/orders/{expiring}/status", new { status = "Processing" }, 409);
            await Call("POST", $"/api/orders/{expiring}/items", new { productId = pid, quantity = 1 }, 409, accessToken: customerToken);
            await Task.WhenAll(Expire(), Expire());
            await Expire();
            Check("Concurrent expiry restores reservation exactly once", await Stock(pid) == 9);
            history = await Call("GET", $"/api/orders/{expiring}/history", null, 200, accessToken: customerToken);
            Check("Expiry records system reason and cancelled status", history!.AsArray().Count == 2 && history[1]!["reason"]!.GetValue<string>() == "ReservationExpired" && history[1]!["actorId"] is null);

            var background = Id(await Call("POST", "/api/orders", Body(pid, 1), 201, Guid.NewGuid().ToString("N")));
            await Sql("UPDATE \"Orders\" SET \"ReservationExpiresAtUtc\"=now()-interval '1 second' WHERE \"Id\"=$1", background);
            bool cancelled = false;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                cancelled = Convert.ToInt32(await Sql("SELECT \"Status\" FROM \"Orders\" WHERE \"Id\"=$1", background)) == 5;
                if (cancelled) break;
                await Task.Delay(250);
            }
            Check("Hosted worker automatically expires pending reservations", cancelled && await Stock(pid) == 9);

            var empty = Id(await Call("POST", "/api/products", new { name = "Unavailable", price = 1, stockQuantity = 0, categoryId = category }, 201));
            long before = await Movements(pid);
            var failedKey = Guid.NewGuid().ToString("N");
            await Call("POST", "/api/orders", new { items = new[] { new { productId = pid, quantity = 1 }, new { productId = empty, quantity = 1 } } }, 400, failedKey);
            Check("Failed creation rolls back stock and journal", await Stock(pid) == 9 && await Movements(pid) == before);
            Check("Failed creation does not consume idempotency key", Convert.ToInt64(await Sql("SELECT count(*) FROM \"OrderRequests\" WHERE \"Key\"=$1", failedKey)) == 0);
            await Call("POST", "/api/orders", Body(pid, 1), 201, failedKey);

            var ledger = await Call("GET", $"/api/stock-movements?productId={pid}&limit=100", null, 200);
            Check("Ledger retains order reference and reason", ledger!.AsArray().Any(x => x!["orderReference"] is not null && x["reason"]!.GetValue<string>().StartsWith("OrderCreated")));
            Check("Ledger balances reconcile", ledger.AsArray().Sum(x => x!["quantityChange"]!.GetValue<long>()) == await Stock(pid));
            await Call("GET", "/api/stock-movements?limit=101", null, 400);
            var firstPage = await Call("GET", $"/api/stock-movements?productId={pid}&limit=1", null, 200);
            var nextPage = await Call("GET", $"/api/stock-movements?productId={pid}&afterId={firstPage![0]!["id"]}&limit=1", null, 200);
            Check("Stock journal supports cursor pagination", nextPage![0]!["id"]!.GetValue<long>() > firstPage[0]!["id"]!.GetValue<long>());
            foreach (var table in new[] { "StockMovements", "OrderStatusEvents" })
            {
                try { await Sql($"UPDATE \"{table}\" SET \"Reason\"='tampered'"); Check(table + " is append-only", false); }
                catch (PostgresException error) { Check(table + " is append-only", error.SqlState == "23514"); }
            }
            await Call("DELETE", $"/api/orders/{oid}", null, 204);
            await Call("GET", $"/api/orders/{oid}", null, 404);
            await Call("GET", $"/api/orders/{oid}/history", null, 200);
            Check("Deleted order retains audit history", Convert.ToInt64(await Sql("SELECT count(*) FROM \"OrderStatusEvents\" WHERE \"OrderId\"=$1", oid)) == 4);
            await Call("POST", "/api/orders", Body(pid, 2), 409, key);
        }
        catch (Exception error) { Check("Hardening execution: " + error.Message, false); }
        report.Insert(0, $"Hardening: {report.Count} checks; failures: {failures}");
        await File.WriteAllLinesAsync(reportPath, report);
        Console.WriteLine(report[0]);
        foreach (var line in report.Where(x => x.StartsWith("FAIL"))) Console.WriteLine(line);
        return failures;
    }

    private sealed class SystemUser : ICurrentUser
    {
        public int Id => 0;
        public bool IsAdmin => false;
        public bool IsStaff => false;
    }
}
