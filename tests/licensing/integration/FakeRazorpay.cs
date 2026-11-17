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
    private readonly Dictionary<string, JsonObject> collectionPatches = [];
    private int sequence;

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UnixEpoch.AddDays(1);
    internal bool LoseCreateResponse { get; set; }
    internal bool Redirect { get; set; }
    internal bool LengthlessOversize { get; set; }
    /// <summary>The next <c>n</c> reads of an order entity answer 503, as a transient provider outage would.</summary>
    internal int FailOrderReads { get; set; }
    internal int OversizeBytesServed { get; private set; }
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
        ["Payments:Razorpay:MaxAmountPaise"] = "50000000",
        ["Payments:Razorpay:Methods:0"] = methods ?? "card",
        ["Payments:Razorpay:Methods:1"] = "upi",
    };

    internal JsonObject Payment(string id) { lock (gate) return payments.Single(p => p["id"]!.GetValue<string>() == id); }
    internal string[] OrderIds() { lock (gate) return [.. orders.Keys.Order(StringComparer.Ordinal)]; }
    internal string OrderId() { lock (gate) return orders.Keys.Single(); }

    /// <summary>Changes one field of a payment only as the order's payment collection reports it, not its own entity.</summary>
    internal void PatchCollectionEntry(string paymentId, string field, JsonNode? value)
    {
        lock (gate)
        {
            if (!collectionPatches.TryGetValue(paymentId, out var patch))
                collectionPatches[paymentId] = patch = [];
            patch[field] = value;
        }
    }
    internal JsonObject Order(string id) { lock (gate) return orders[id]; }

    internal string AddPayment(string orderId, string status, long? amount = null, string? currency = null, string? onOrder = null,
        string method = "upi", long? createdAt = null, bool withAmountCaptured = true)
    {
        lock (gate)
        {
            var order = orders[orderId];
            var id = $"pay_fixture{++sequence:D6}";
            payments.Add(new JsonObject
            {
                ["id"] = id, ["entity"] = "payment", ["order_id"] = onOrder ?? orderId, ["status"] = status,
                ["captured"] = status == "captured", ["method"] = method,
                ["amount"] = amount ?? order["amount"]!.GetValue<long>(), ["currency"] = currency ?? order["currency"]!.GetValue<string>(),
                ["created_at"] = createdAt ?? Clock().ToUnixTimeSeconds(),
            });
            // Payment fetches document amount_refunded and refund_status; amount_captured is not always present.
            var added = payments[^1];
            added["amount_refunded"] = 0;
            added["refund_status"] = null;
            if (status == "captured" && withAmountCaptured)
                added["amount_captured"] = added["amount"]!.GetValue<long>();
            if (status == "captured" && onOrder is null)
            {
                order["amount_paid"] = order["amount"]!.GetValue<long>();
                order["amount_due"] = 0;
                order["status"] = "paid";
            }
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
            if (LengthlessOversize)
                return Oversize();
            if (Redirect)
                return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://attacker.example/") } };
            return Route(request, body);
        }
    }

    private HttpResponseMessage Oversize() => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new CountingEndlessStream(served => OversizeBytesServed = served)),
    };

    private sealed class CountingEndlessStream(Action<int> report) : Stream
    {
        private int served;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Fill(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => ValueTask.FromResult(Fill(buffer.Span));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Fill(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, 4096);
            buffer[..count].Fill((byte)' ');
            served += count;
            report(served);
            return count;
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
            ["v1", "orders", var id] when orders.ContainsKey(id) && FailOrderReads > 0 => FailOrderRead(),
            ["v1", "orders", var id] when orders.TryGetValue(id, out var order) => Json(HttpStatusCode.OK, Copy(order)),
            ["v1", "orders", var id, "payments"] when orders.ContainsKey(id) =>
                Collection(payments.Where(p => p["order_id"]!.GetValue<string>() == id).Select(Patched)),
            ["v1", "payments", var id] when payments.SingleOrDefault(p => p["id"]!.GetValue<string>() == id) is { } payment =>
                Json(HttpStatusCode.OK, Copy(payment)),
            _ => Json(HttpStatusCode.NotFound, new JsonObject()),
        };
    }

    private HttpResponseMessage FailOrderRead()
    {
        FailOrderReads--;
        return Json(HttpStatusCode.ServiceUnavailable, new JsonObject());
    }

    private JsonObject Patched(JsonObject payment)
    {
        var copy = Copy(payment);
        if (collectionPatches.TryGetValue(payment["id"]!.GetValue<string>(), out var patch))
            foreach (var (field, value) in patch)
                copy[field] = value?.DeepClone();
        return copy;
    }

    private HttpResponseMessage Create(JsonObject input)
    {
        var id = $"order_fixture{++sequence:D6}";
        var order = new JsonObject
        {
            ["id"] = id, ["entity"] = "order", ["amount"] = input["amount"]!.GetValue<long>(),
            ["currency"] = input["currency"]!.GetValue<string>(), ["receipt"] = input["receipt"]!.GetValue<string>(),
            ["partial_payment"] = input["partial_payment"]?.GetValue<bool>() ?? false, ["status"] = "created",
            ["amount_paid"] = 0, ["amount_due"] = input["amount"]!.GetValue<long>(), ["notes"] = input["notes"]!.DeepClone(), ["created_at"] = Clock().ToUnixTimeSeconds(),
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
