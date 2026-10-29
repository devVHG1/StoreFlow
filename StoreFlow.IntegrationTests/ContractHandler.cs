using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StoreFlow.IntegrationTests;

// Старі сценарії використовують актуальний контракт; конфлікти перевіряються окремо.
internal sealed class ContractHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && path == "/api/orders" && !request.Headers.Contains("Idempotency-Key"))
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        if (request.Method == HttpMethod.Put && path.StartsWith("/api/products/") && request.Content is not null)
        {
            JsonObject? body = null;
            try { body = JsonNode.Parse(await request.Content.ReadAsStringAsync(ct)) as JsonObject; }
            catch (System.Text.Json.JsonException) { }
            if (body is not null && !body.ContainsKey("version"))
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);
                get.Headers.Authorization = request.Headers.Authorization;
                using var response = await base.SendAsync(get, ct);
                if (response.IsSuccessStatusCode)
                {
                    var product = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
                    body["version"] = product["version"]!.DeepClone();
                    body["stockChangeReason"] ??= "QA stock adjustment";
                    request.Content = JsonContent.Create(body);
                }
            }
        }
        return await base.SendAsync(request, ct);
    }
}
