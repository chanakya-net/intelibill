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
internal sealed partial class FakeRazorpay(string fixtureKeyId = FakeRazorpay.KeyId, string fixtureKeySecret = FakeRazorpay.KeySecret, int sequenceStart = 0) : HttpMessageHandler
{
    internal const string KeyId = "rzp_test_fixture";
    internal const string KeySecret = "fixture-secret";

    private readonly object gate = new();
    private readonly Dictionary<string, JsonObject> orders = [];
    private readonly List<JsonObject> payments = [];
    private readonly Dictionary<string, JsonObject> collectionPatches = [];
    private readonly Dictionary<string, HashSet<string>> collectionOmissions = [];
    private readonly HashSet<string> hiddenFromCollection = [];
    private readonly HashSet<string> duplicatedInCollection = [];
    private int sequence = sequenceStart;

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UnixEpoch.AddDays(1);
    internal bool LoseCreateResponse { get; set; }
    internal bool Redirect { get; set; }
    internal bool LengthlessOversize { get; set; }
    /// <summary>The next <c>n</c> reads of an order entity answer 503, as a transient provider outage would.</summary>
    internal int FailOrderReads { get; set; }
    /// <summary>The next <c>n</c> reads of a payment entity answer 503, as a transient provider outage would.</summary>
    internal int FailPaymentReads { get; set; }
    /// <summary>The next <c>n</c> direct reads of a refund entity answer 503, as a transient provider outage would.</summary>
    internal int FailRefundGets { get; set; }
    /// <summary>Added to the <c>count</c> every collection answer states, to model a truncated or inconsistent page.</summary>
    internal int CollectionCountOffset { get; set; }
    internal int OversizeBytesServed { get; private set; }
    /// <summary>The first call matching this answers its headers, then the connection ends mid-body.</summary>
    internal Func<string, bool>? TruncateWhen { get; set; }
    /// <summary>The first call matching this computes its answer, then waits for <see cref="ReleaseHeld"/>.</summary>
    internal Func<string, bool>? HoldWhen { get; set; }
    internal TaskCompletionSource HoldReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Every call matching this never answers: it waits until its caller gives up or is interrupted.</summary>
    internal Func<string, bool>? StallWhen { get; set; }
    internal TaskCompletionSource StallReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ReleaseHeld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    /// <summary>Drops one field of a payment only from the order's payment collection entry.</summary>
    internal void OmitFromCollectionEntry(string paymentId, string field)
    {
        lock (gate)
        {
            if (!collectionOmissions.TryGetValue(paymentId, out var omitted))
                collectionOmissions[paymentId] = omitted = [];
            omitted.Add(field);
        }
    }
    internal JsonObject Order(string id) { lock (gate) return orders[id]; }

    /// <summary>Leaves a payment out of its order's payment collection while the order still counts its attempt.</summary>
    internal void HideFromCollection(string paymentId) { lock (gate) hiddenFromCollection.Add(paymentId); }
    internal void RevealInCollection(string paymentId) { lock (gate) hiddenFromCollection.Remove(paymentId); }
    /// <summary>Lists a payment twice in its order's payment collection, as a repeated page entry would.</summary>
    internal void DuplicateInCollection(string paymentId) { lock (gate) duplicatedInCollection.Add(paymentId); }

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
            if (onOrder is null)
            {
                // Razorpay counts every payment attempt on the order and marks a created order attempted.
                order["attempts"] = (order["attempts"]?.GetValue<int>() ?? 0) + 1;
                if (order["status"]?.GetValue<string>() == "created")
                    order["status"] = "attempted";
            }
            if (status == "captured" && onOrder is null)
            {
                order["amount_paid"] = order["amount"]!.GetValue<long>();
                order["amount_due"] = 0;
                order["status"] = "paid";
            }
            return id;
        }
    }

    /// <summary>Moves an existing payment to a new status in place, as Razorpay's own state machine would.</summary>
    internal void Transition(string paymentId, string status)
    {
        lock (gate)
        {
            var payment = payments.Single(p => p["id"]!.GetValue<string>() == paymentId);
            (payment["status"], payment["captured"]) = (status, status == "captured");
            if (status != "captured")
                return;
            var order = orders[payment["order_id"]!.GetValue<string>()];
            payment["amount_captured"] = payment["amount"]!.GetValue<long>();
            (order["amount_paid"], order["amount_due"], order["status"]) = (order["amount"]!.GetValue<long>(), 0, "paid");
        }
    }

    internal static string Sign(string orderId, string paymentId) => Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(KeySecret), Encoding.UTF8.GetBytes($"{orderId}|{paymentId}")));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{fixtureKeyId}:{fixtureKeySecret}"));
        if (request.Headers.Authorization is not { Scheme: "Basic", Parameter: { } given } || given != expected)
            return Json(HttpStatusCode.Unauthorized, new JsonObject());
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        HttpResponseMessage response;
        bool hold, stall;
        lock (gate)
        {
            var call = $"{request.Method} {request.RequestUri!.PathAndQuery}";
            Calls.Add(call);
            if (LengthlessOversize)
                return Oversize();
            if (Redirect)
                return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://attacker.example/") } };
            response = Route(request, body);
            if (TruncateWhen?.Invoke(call) == true)
            {
                TruncateWhen = null;
                response.Content = new StreamContent(new EndedStream());
            }
            hold = HoldWhen?.Invoke(call) == true;
            if (hold)
                HoldWhen = null;
            stall = StallWhen?.Invoke(call) == true;
        }
        if (stall)
        {
            StallReached.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
        if (hold)
        {
            HoldReached.TrySetResult();
            await ReleaseHeld.Task.WaitAsync(ct);
        }
        return response;
    }

    /// <summary>A body whose connection closes early, as .NET reports a premature end of a response.</summary>
    private sealed class EndedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw Ended();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromException<int>(Ended());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private static HttpIOException Ended() => new(HttpRequestError.ResponseEnded, "fixture: response ended prematurely");
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
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (RefundRoute(request, parts, body) is { } refund) return refund;
        if (request.Method == HttpMethod.Post && path == "/v1/orders")
            return Create(JsonNode.Parse(body!)!.AsObject());
        if (request.Method != HttpMethod.Get)
            return Json(HttpStatusCode.BadRequest, new JsonObject());
        if (path == "/v1/orders")
            return Collection(orders.Values.Where(o => query.Contains($"receipt={o["receipt"]}", StringComparison.Ordinal)));
        return parts switch
        {
            ["v1", "orders", var id] when orders.ContainsKey(id) && FailOrderReads > 0 => FailOrderRead(),
            ["v1", "orders", var id] when orders.TryGetValue(id, out var order) => Json(HttpStatusCode.OK, Copy(order)),
            ["v1", "orders", var id, "payments"] when orders.ContainsKey(id) =>
                Collection(OrderPayments(id).Select(Patched)),
            ["v1", "payments", var id] when payments.Any(p => p["id"]!.GetValue<string>() == id) && FailPaymentReads > 0 =>
                FailPaymentRead(),
            ["v1", "payments", var id] when payments.SingleOrDefault(p => p["id"]!.GetValue<string>() == id) is { } payment =>
                Json(HttpStatusCode.OK, Copy(payment)),
            _ => Json(HttpStatusCode.NotFound, new JsonObject()),
        };
    }

    private IEnumerable<JsonObject> OrderPayments(string orderId) =>
        payments.Where(p => p["order_id"]!.GetValue<string>() == orderId && !hiddenFromCollection.Contains(p["id"]!.GetValue<string>()))
            .SelectMany(p => duplicatedInCollection.Contains(p["id"]!.GetValue<string>()) ? new[] { p, p } : [p]);

    private HttpResponseMessage FailOrderRead()
    {
        FailOrderReads--;
        return Json(HttpStatusCode.ServiceUnavailable, new JsonObject());
    }

    private HttpResponseMessage FailPaymentRead()
    {
        FailPaymentReads--;
        return Json(HttpStatusCode.ServiceUnavailable, new JsonObject());
    }

    private JsonObject Patched(JsonObject payment)
    {
        var copy = Copy(payment);
        if (collectionPatches.TryGetValue(payment["id"]!.GetValue<string>(), out var patch))
            foreach (var (field, value) in patch)
                copy[field] = value?.DeepClone();
        if (collectionOmissions.TryGetValue(payment["id"]!.GetValue<string>(), out var omitted))
            foreach (var field in omitted)
                copy.Remove(field);
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
            ["attempts"] = 0, ["amount_paid"] = 0, ["amount_due"] = input["amount"]!.GetValue<long>(), ["notes"] = input["notes"]!.DeepClone(), ["created_at"] = Clock().ToUnixTimeSeconds(),
        };
        orders[id] = order;
        if (LoseCreateResponse)
            throw new HttpRequestException("fixture: response lost after the provider created the order");
        return Json(HttpStatusCode.OK, Copy(order));
    }

    private static JsonObject Copy(JsonObject value) => (JsonObject)value.DeepClone();

    private HttpResponseMessage Collection(IEnumerable<JsonObject> items)
    {
        var array = new JsonArray([.. items.Select(Copy)]);
        return Json(HttpStatusCode.OK, new JsonObject
        { ["entity"] = "collection", ["count"] = array.Count + CollectionCountOffset, ["items"] = array });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject value) =>
        new(status) { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
}
