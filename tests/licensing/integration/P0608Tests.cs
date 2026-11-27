using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-08: a billing member requests a refund against a verified payment and sees its status without treating the
/// request as approval, money movement or suspension. Real PostgreSQL, real merchant and licensing APIs, local
/// simulator receipts and fixed clocks; no real provider is contacted.
/// </summary>
public sealed partial class P0608Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static readonly DateTimeOffset RequestedInstant = P0601Tests.January31;
    internal static readonly string[] ShopNames = ["Refund Central", "Refund East"];

    [Fact]
    public async Task MemberRequestsRefundWithoutChangingTermAccessBalanceOrShopStatus()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        await DrainAsync(fixture);
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var before = await SnapshotAsync(fixture, package, term);

        var key = Guid.NewGuid();
        using var response = await PostRefundAsync(package.Payer.Client, package.Account,
            RequestBody(purchase.OrderId, confirmation, 4500L, "Duplicate shop setup before go-live", key));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.True(response.Headers.CacheControl?.NoStore);

        using var document = JsonDocument.Parse(body);
        var created = document.RootElement;
        Assert.Equal(1, created.GetProperty("schemaVersion").GetInt32());
        Assert.NotEqual(Guid.Empty, created.GetProperty("refundRequestId").GetGuid());
        Assert.Equal(package.Account, created.GetProperty("accountId").GetGuid());
        Assert.Equal(purchase.OrderId, created.GetProperty("orderId").GetGuid());
        Assert.Equal(confirmation, created.GetProperty("confirmationId").GetGuid());
        Assert.Equal("Requested", created.GetProperty("status").GetString());
        Assert.Equal(4500L, created.GetProperty("amountPaise").GetInt64());
        Assert.Equal(12000L, created.GetProperty("paymentAmountPaise").GetInt64());
        Assert.Equal("INR", created.GetProperty("currency").GetString());
        Assert.Equal("Duplicate shop setup before go-live", created.GetProperty("reason").GetString());
        Assert.Equal(RequestedInstant, created.GetProperty("requestedAt").GetDateTimeOffset());
        Assert.False(created.TryGetProperty("providerId", out _));
        Assert.False(created.TryGetProperty("providerAccountId", out _));
        Assert.False(created.TryGetProperty("providerPaymentReference", out _));

        Assert.Equal(before, await SnapshotAsync(fixture, package, term));
        Assert.Equal(1L, await fixture.CountAsync("refund_requests"));
    }

    internal static string RefundsPath(Guid account) => $"/api/billing/accounts/{account:D}/refund-requests";

    internal static object RequestBody(Guid orderId, Guid confirmationId, long amountPaise, string reason, Guid? key = null) => new
    {
        operationKey = key ?? Guid.NewGuid(),
        orderId,
        confirmationId,
        amountPaise,
        reason,
    };

    internal static Task<HttpResponseMessage> PostRefundAsync(HttpClient client, Guid account, object body) =>
        client.PostAsJsonAsync(RefundsPath(account), body);

    internal static async Task<Guid> ConfirmationIdAsync(HttpClient client, Guid account, Guid order)
    {
        using var response = await client.GetAsync($"/api/billing/accounts/{account:D}/orders/{order:D}/payment/confirmations");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        return Assert.Single(doc.RootElement.EnumerateArray()).GetProperty("confirmationId").GetGuid();
    }

    internal static async Task DrainAsync(P0308Fixture fixture)
    {
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
    }

    internal static async Task<string> SnapshotAsync(P0308Fixture fixture, Package package, Guid term)
    {
        var shopStatuses = new List<string>();
        foreach (var shop in package.Shops)
        {
            using var status = await P0209Fixture.ShopStatusAsync(shop.Client, shop.ShopId);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            shopStatuses.Add(await status.Content.ReadAsStringAsync());
        }
        return string.Join('|',
            await P0601Tests.EvidenceAsync(fixture, term),
            await fixture.CountAsync("payment_events"),
            await fixture.CountAsync("payment_confirmations"),
            await fixture.CountAsync("operational_restrictions"),
            await fixture.CountAsync("entitlement_snapshots"),
            string.Join("||", shopStatuses));
    }
}
