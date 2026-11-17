using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0305Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal const string Secret = "fixture-webhook-secret-868";
    internal const string Account = "acc_fixture868";
    private const string Path = "/api/payments/webhooks/razorpay";
    private const string PayerMarker = "private-webhook-payer@example.invalid";

    [Fact]
    public async Task SignedEventIsStoredOnceAndAcknowledgedBeforeAnyProcessing()
    {
        await using var rig = await StartAsync();
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var calls = rig.Fake.Calls.Count;
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        var body = Body(rig, order, payment);

        using var response = await SendAsync(client, body, "event_1");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendAsync(client, body, "event_1")));
        foreach (var duplicate in duplicates)
        {
            using (duplicate) Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        }
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_events"));
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_work", "status = 'Pending'"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(calls, rig.Fake.Calls.Count);
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("api-secret")]
    [InlineData("tampered")]
    [InlineData("missing-signature")]
    [InlineData("duplicate-signature")]
    [InlineData("missing-event")]
    [InlineData("wrong-account")]
    public async Task ForgedOrTamperedDeliveryIsRefusedBeforeStorageOrProcessing(string variant)
    {
        await using var rig = await StartAsync();
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var calls = rig.Fake.Calls.Count;
        var body = Body(rig, order, "pay_fixture", account: variant == "wrong-account" ? "acc_foreign" : Account);
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        using var request = Delivery(body, variant == "missing-event" ? null : "event_1",
            variant == "missing-signature" ? null : variant == "api-secret" ? FakeRazorpay.KeySecret :
            variant == "forged" ? "wrong-secret" : Secret);
        if (variant == "tampered")
            request.Content = new StringContent(body + " ", Encoding.UTF8, "application/json");
        if (variant == "duplicate-signature")
            request.Headers.Add("X-Razorpay-Signature", Sign(body, Secret));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0L, await rig.Fixture.CountAsync("gateway_inbox_events"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(calls, rig.Fake.Calls.Count);
    }

    private Task<P0304Tests.Rig> StartAsync(IReadOnlyDictionary<string, string?>? extra = null,
        Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Payments:Razorpay:WebhookSecret"] = Secret,
            ["Payments:Razorpay:WebhookAccountId"] = Account
        };
        if (extra is not null)
            foreach (var (key, value) in extra) settings[key] = value;
        return P0304Tests.Rig.StartAsync(database, extraSettings: settings, configure: configure);
    }

    private static string Body(P0304Tests.Rig rig, string order, string payment, string kind = "payment.captured",
        string account = Account) => Body(rig.Quote.AmountPaise, rig.Fixture.CheckoutClock.Now, order, payment, kind, account);

    internal static string Body(long amount, DateTimeOffset at, string order, string payment, string kind = "payment.captured",
        string account = Account) => System.Text.Json.JsonSerializer.Serialize(new
    {
        entity = "event", account_id = account, @event = kind,
        created_at = at.ToUnixTimeSeconds(),
        payload = new
        {
            order = kind == "order.paid" ? new { entity = new { entity = "order", id = order } } : null,
            payment = new
            {
                entity = new
                {
                    entity = "payment", id = payment, order_id = order,
                    amount, currency = "INR", status = "captured", captured = true,
                    created_at = at.ToUnixTimeSeconds(), email = PayerMarker,
                    contact = "fixture-private-868-phone", vpa = "fixture-private-868@upi",
                    card = new { name = "fixture-private-868-cardholder" }
                }
            }
        }
    });

    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client, string body, string eventId,
        string secret = Secret)
    {
        using var request = Delivery(body, eventId, secret);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage Delivery(string body, string? eventId, string? secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (eventId is not null) request.Headers.Add("X-Razorpay-Event-Id", eventId);
        if (secret is not null) request.Headers.Add("X-Razorpay-Signature", Sign(body, secret));
        return request;
    }

    private static string Sign(string body, string key) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body)));
}
