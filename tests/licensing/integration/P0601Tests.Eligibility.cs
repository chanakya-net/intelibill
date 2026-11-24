using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    /// <summary>
    /// Only a positive rounded charge inside the current interval is payable. Zero, negative and rounded-zero changes
    /// offer renewal or courtesy; grace needs a renewal and after grace a new term. Nothing becomes a checkout order.
    /// </summary>
    [Fact]
    public async Task EligibilityOffersOnlyTheApplicableNextSteps()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var baseline = await EvidenceAsync(fixture, term);
        var wider = await PublishAsync(fixture, 12000L, members: 10);
        var cheaper = await PublishAsync(fixture, 9000L, members: 10);
        var slightly = await PublishAsync(fixture, 12001L, members: 10);
        fixture.At(February14);

        await AssertDeferredAsync(package, term, wider, 3, "NonPositiveDifference", 0L);
        await AssertDeferredAsync(package, term, cheaper, 3, "NonPositiveDifference", -3000L);
        var small = await QuoteAsync(package, term, Request(slightly, 3, 3, February29));
        Assert.Equal(("Payable", 1L), (small.GetProperty("outcome").GetString(), small.GetProperty("amountPaise").GetInt64()));
        fixture.At(February29.AddDays(-1));
        await AssertDeferredAsync(package, term, slightly, 3, "RoundedToZero", 1L);

        fixture.At(February29.AddDays(2));
        var grace = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        AssertNextStep(grace, "RenewalRequired", "Renew");
        fixture.At(February29.AddDays(7));
        var expired = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        AssertNextStep(expired, "NewTermRequired", "PurchaseNewTerm");
        Assert.Equal(baseline, await EvidenceAsync(fixture, term));
    }

    [Fact]
    public async Task RequestsThatAreNotIncreasesOrDoNotMatchTheTermAreRefused()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var fewerMembers = await PublishAsync(fixture, 30000L, members: 3);
        fixture.At(February14);

        await AssertRefusedAsync(package, term, Request(package.Plan, 3, 3, February29), HttpStatusCode.BadRequest, "Billing.CapacityChangeInvalid");
        await AssertRefusedAsync(package, term, Request(package.Plan, 2, 3, February29), HttpStatusCode.BadRequest, "Billing.CapacityChangeInvalid");
        await AssertRefusedAsync(package, term, Request(fewerMembers, 4, 3, February29), HttpStatusCode.BadRequest, "Billing.CapacityChangeInvalid");
        await AssertRefusedAsync(package, term, Request(Guid.NewGuid(), 4, 3, February29), HttpStatusCode.BadRequest, "Billing.CapacityChangeInvalid");
        await AssertRefusedAsync(package, term, Request(package.Plan, 4, 2, February29), HttpStatusCode.Conflict, "Billing.CapacityChangeSourceChanged");
        await AssertRefusedAsync(package, term, Request(package.Plan, 4, 3, February14), HttpStatusCode.Conflict, "Billing.CapacityChangeSourceChanged");
        await AssertRefusedAsync(package, Guid.NewGuid(), Request(package.Plan, 4, 3, February29), HttpStatusCode.NotFound, "Billing.CapacityChangeNotFound");
        Assert.Equal(0L, await fixture.CountAsync("capacity_change_quotes"));
    }

    [Fact]
    public async Task TrialUpgradeConvertsThroughAFullPaidPurchase()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(1, [ShopNames[0]]);
        await P0309Tests.ApplyTrialAsync(fixture, package.Payer);
        var trialTerm = (Guid)(await fixture.ScalarAsync(
            $"SELECT term_id FROM trial_history WHERE shop_id = '{package.Payer.ShopId:D}'"))!;
        var paidThrough = new DateTimeOffset(DateTime.SpecifyKind((DateTime)(await fixture.ScalarAsync(
            $"SELECT ends_at FROM subscription_coverage_terms WHERE term_id = '{trialTerm:D}'"))!, DateTimeKind.Utc));
        var capacity = (int)(await fixture.ScalarAsync(
            $"SELECT capacity FROM subscription_coverage_terms WHERE term_id = '{trialTerm:D}'"))!;
        var target = await PublishAsync(fixture, 12000L, members: 100);

        var quote = await QuoteAsync(package, trialTerm, Request(target, 3, capacity, paidThrough));
        AssertNextStep(quote, "TrialConversion", "PurchasePaid");
        Assert.Equal(12000L, quote.GetProperty("targetGrossPaise").GetInt64());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("purchasedGrossPaise").ValueKind);
        Assert.Equal(0L, await fixture.CountAsync("checkout_orders"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
    }

    private static async Task AssertDeferredAsync(Package package, Guid term, Guid target, int capacity, string reason,
        long difference)
    {
        var quote = await QuoteAsync(package, term, Request(target, capacity, 3, February29));
        Assert.Equal(("AtRenewalOrCourtesy", reason), (quote.GetProperty("outcome").GetString(), quote.GetProperty("reason").GetString()));
        Assert.Equal(difference, quote.GetProperty("differencePaise").GetInt64());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("amountPaise").ValueKind);
        Assert.Equal(["ScheduleAtRenewal", "RequestCourtesy"], Actions(quote));
    }

    private static void AssertNextStep(JsonElement quote, string outcome, string action)
    {
        Assert.Equal(outcome, quote.GetProperty("outcome").GetString());
        Assert.Equal([action], Actions(quote));
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("amountPaise").ValueKind);
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("remainingSeconds").ValueKind);
    }

    private static string[] Actions(JsonElement quote) =>
        [.. quote.GetProperty("actions").EnumerateArray().Select(action => action.GetString()!)];

    internal static async Task<JsonElement> QuoteAsync(Package package, Guid term, object request)
    {
        using var response = await PostQuoteAsync(package, term, request);
        return await P0209Fixture.ReadAsync(response);
    }

    private static async Task AssertRefusedAsync(Package package, Guid term, object request, HttpStatusCode status, string code)
    {
        using var response = await PostQuoteAsync(package, term, request);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await P0206Fixture.CodeAsync(response));
    }

    private static Task<Guid> PublishAsync(P0308Fixture fixture, long monthly, int members) =>
        P0301Tests.PublishPlanAsync(fixture.Checkout.Trial, membersPerShop: members, monthlyPricePaise: monthly);
}
