using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    [Theory]
    [InlineData(12191L, null, "AtRenewalOrCourtesy", "BelowProviderMinimum")]
    [InlineData(12193L, 100L, "Payable", null)]
    [InlineData(100012000L, null, "PaymentUnavailable", "AmountUnsupported")]
    public async Task RazorpayAmountBoundariesAreCheckedLocally(long targetGross, long? amount, string outcome, string? reason)
    {
        using var transport = new FakeRazorpay();
        await using var fixture = await StartAsync(database, January31, FakeRazorpay.Settings(), services: services =>
            services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => transport));
        var (package, term) = await PackageWithRecordedTermAsync(fixture);
        var target = await PublishAsync(fixture, targetGross, members: 10);
        fixture.At(February14);

        var request = Request(target, 3, 3, February29);
        var quote = await QuoteAsync(package, term, request);
        Assert.Equal(outcome, quote.GetProperty("outcome").GetString());
        Assert.Equal(reason, quote.GetProperty("reason").GetString());
        Assert.Equal(amount, quote.GetProperty("amountPaise").Deserialize<long?>());
        if (reason == "BelowProviderMinimum")
            Assert.Equal(["ScheduleAtRenewal", "RequestCourtesy"], Actions(quote));
        Assert.Equal(amount is null ? string.Empty : "razorpay",
            await fixture.ScalarAsync("SELECT coalesce(provider_id, '') FROM capacity_change_quotes"));
        Assert.Equal(quote.GetRawText(), (await QuoteAsync(package, term, request)).GetRawText());
        Assert.Equal(0L, await fixture.CountAsync("checkout_orders"));
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public async Task PositiveIncreaseWithNoProviderRemainsAQuoteWithoutAnAmount()
    {
        await using var fixture = await StartAsync(database, January31,
            new Dictionary<string, string?> { ["Payments:Mode"] = "Off" });
        var (package, term) = await PackageWithRecordedTermAsync(fixture);

        var quote = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        Assert.Equal("PaymentUnavailable", quote.GetProperty("outcome").GetString());
        Assert.Equal("ProviderUnavailable", quote.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("amountPaise").ValueKind);
        Assert.Equal(0L, await fixture.CountAsync("checkout_orders"));
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("Shop")]
    public async Task ActiveSuspensionIsDisclosedWithoutItsInternalReason(string scope)
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term) = await PurchaseForRestrictionAsync(fixture);
        using var security = await P0417Tests.SignInAsync(fixture.Checkout.Delivery, "security-administrator");
        var path = $"/api/admin/accounts/{package.Account:D}/restrictions";
        await P0404Tests.SuspendAsync(security, path, scope, scope == "Shop" ? package.Payer.ShopId : null, "Trading");

        var request = Request(package.Plan, 4, 3, February29);
        var quote = await QuoteAsync(package, term, request);
        Assert.True(quote.GetProperty("suspensionDisclosed").GetBoolean());
        Assert.DoesNotContain("Security review.", quote.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(quote.GetRawText(), (await QuoteAsync(package, term, request)).GetRawText());
        Assert.Equal(1L, await fixture.CountAsync("checkout_orders"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedShopOrAccountNeedsReview(bool accountWide)
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term) = await PurchaseForRestrictionAsync(fixture);
        await CloseForQuoteAsync(package, accountWide);
        Assert.Equal(1L, await fixture.CountAsync("active_operational_closure_targets"));

        var request = Request(package.Plan, 4, 3, February29);
        await AssertRefusedAsync(package, term, request, HttpStatusCode.Conflict, "Billing.CapacityChangeNeedsReview");
        await AssertRefusedAsync(package, term, request, HttpStatusCode.Conflict, "Billing.CapacityChangeNeedsReview");
        Assert.Equal(0L, await fixture.CountAsync("capacity_change_quotes"));
    }

    private static async Task<(Package Package, Guid Term)> PurchaseForRestrictionAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(1, [ShopNames[0]]);
        var (_, term) = await PurchaseAsync(package, "Month");
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        return (package, term);
    }

    private static async Task CloseForQuoteAsync(Package package, bool accountWide)
    {
        var owner = package.Payer;
        if (accountWide)
        {
            var proposal = await P0416Tests.ProposeAsync(owner, package.Account);
            await P0416Tests.ConsentAsync(owner, proposal.GetProperty("id").GetGuid());
            using var response = await P0416Tests.ConfirmAccountAsync(owner, package.Account, proposal);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return;
        }

        var path = $"/api/licensing/shops/{owner.ShopId:D}/closure";
        var review = await owner.Client.GetFromJsonAsync<JsonElement>(path + "-preview");
        using var closed = await owner.Client.PostAsJsonAsync(path, new
        {
            operationKey = Guid.NewGuid(), expectedVersion = review.GetProperty("closureVersion").GetInt64(),
            ownerVersion = review.GetProperty("ownerVersion").GetInt64(), workDigest = review.GetProperty("workDigest").GetString(),
            reviewedKnownWork = true, consideredDeviceQueues = true, financialTreatment = "NoFinancialChange",
        });
        Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
    }
}
