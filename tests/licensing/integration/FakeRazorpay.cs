using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// In-process stand-in for the documented Razorpay order and payment endpoints. It is the only transport the
/// adapter sees in tests: nothing here reaches api.razorpay.com and every credential is a fixture value.
/// </summary>
internal sealed class FakeRazorpay : HttpMessageHandler
{
    internal const string KeyId = "rzp_test_fixture";
    internal const string KeySecret = "fixture-secret";

    private readonly object gate = new();
    private readonly Dictionary<string, JsonObject> orders = [];
    private readonly List<JsonObject> payments = [];
    private int sequence;

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UnixEpoch.AddDays(1);
    internal bool LoseCreateResponse { get; set; }
    internal List<string> Calls { get; } = [];
    internal int Posts { get { lock (gate) return Calls.Count(c => c.StartsWith("POST ", StringComparison.Ordinal)); } }

    internal static Dictionary<string, string?> Settings(string? methods = null) => new()
    {
        ["Payments:Mode"] = "Razorpay",
        ["Payments:ReconcileInBackground"] = "false",
        ["Payments:Razorpay:KeyId"] = KeyId,
        ["Payments:Razorpay:KeySecret"] = KeySecret,
        ["Payments:Razorpay:AccountId"] = "fixture-account",
        ["Payments:Razorpay:Environment"] = "Test",
        ["Payments:Razorpay:ConfigurationVersion"] = "fixture/1",
        ["Payments:Razorpay:BaseUrl"] = "https://razorpay.fixture.invalid/",
        ["Payments:Razorpay:Methods:0"] = methods ?? "card",
        ["Payments:Razorpay:Methods:1"] = "upi",
    };

    internal string OrderId() { lock (gate) return orders.Keys.Single(); }
    internal JsonObject Order(string id) { lock (gate) return orders[id]; }

    internal string AddPayment(string orderId, string status, long? amount = null, string? currency = null, string? onOrder = null)
    {
        lock (gate)
        {
            var order = orders[orderId];
            var id = $"pay_fixture{++sequence:D6}";
            payments.Add(new JsonObject
            {
                ["id"] = id, ["entity"] = "payment", ["order_id"] = onOrder ?? orderId, ["status"] = status,
                ["captured"] = status == "captured", ["method"] = "upi",
                ["amount"] = amount ?? order["amount"]!.GetValue<long>(), ["currency"] = currency ?? order["currency"]!.GetValue<string>(),
                ["created_at"] = Clock().ToUnixTimeSeconds(),
            });
            return id;
        }
    }

    internal static string Sign(string orderId, string paymentId) => Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(KeySecret), Encoding.UTF8.GetBytes($"{orderId}|{paymentId}")));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}"));
        if (request.Headers.Authorization is not { Scheme: "Basic", Parameter: { } given } || given != expected)
            return Json(HttpStatusCode.Unauthorized, new JsonObject());
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        lock (gate)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return Route(request, body);
        }
    }

    private HttpResponseMessage Route(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
        var query = request.RequestUri.Query;
        if (request.Method == HttpMethod.Post && path == "/v1/orders")
            return Create(JsonNode.Parse(body!)!.AsObject());
        if (request.Method != HttpMethod.Get)
            return Json(HttpStatusCode.BadRequest, new JsonObject());
        if (path == "/v1/orders")
            return Collection(orders.Values.Where(o => query.Contains($"receipt={o["receipt"]}", StringComparison.Ordinal)));
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts switch
        {
            ["v1", "orders", var id] when orders.TryGetValue(id, out var order) => Json(HttpStatusCode.OK, Copy(order)),
            ["v1", "orders", var id, "payments"] when orders.ContainsKey(id) =>
                Collection(payments.Where(p => p["order_id"]!.GetValue<string>() == id)),
            ["v1", "payments", var id] when payments.SingleOrDefault(p => p["id"]!.GetValue<string>() == id) is { } payment =>
                Json(HttpStatusCode.OK, Copy(payment)),
            _ => Json(HttpStatusCode.NotFound, new JsonObject()),
        };
    }

    private HttpResponseMessage Create(JsonObject input)
    {
        var id = $"order_fixture{++sequence:D6}";
        var order = new JsonObject
        {
            ["id"] = id, ["entity"] = "order", ["amount"] = input["amount"]!.GetValue<long>(),
            ["currency"] = input["currency"]!.GetValue<string>(), ["receipt"] = input["receipt"]!.GetValue<string>(),
            ["partial_payment"] = input["partial_payment"]?.GetValue<bool>() ?? false, ["status"] = "created",
            ["amount_paid"] = 0, ["notes"] = input["notes"]!.DeepClone(), ["created_at"] = Clock().ToUnixTimeSeconds(),
        };
        orders[id] = order;
        if (LoseCreateResponse)
            throw new HttpRequestException("fixture: response lost after the provider created the order");
        return Json(HttpStatusCode.OK, Copy(order));
    }

    private static JsonObject Copy(JsonObject value) => (JsonObject)value.DeepClone();

    private static HttpResponseMessage Collection(IEnumerable<JsonObject> items)
    {
        var array = new JsonArray([.. items.Select(Copy)]);
        return Json(HttpStatusCode.OK, new JsonObject { ["entity"] = "collection", ["count"] = array.Count, ["items"] = array });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject value) =>
        new(status) { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
}
