using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Domain.Policies;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    [Fact]
    public async Task ExtendedTrialUsesTheEndShownInAccountStatusAndReplays()
    {
        await using var fixture = await P0308Fixture.StartAsync(database, January31);
        var package = await fixture.PackageAsync(1, [ShopNames[0]]);
        var trial = await StartTrialAsync(fixture, package);
        var extendedEnd = trial.End.AddDays(4).AddTicks(1234560);
        await ExtendTrialAsync(fixture, package, extendedEnd);
        fixture.At(extendedEnd.AddMinutes(-5));

        var observed = (await AccountTermAsync(package, trial.Term)).GetProperty("paidThrough").GetDateTimeOffset();
        Assert.Equal(extendedEnd, observed);
        var target = await PublishAsync(fixture, 12000L, members: 100);
        var request = Request(target, 3, 1, observed);
        var quote = await QuoteAsync(package, trial.Term, request);
        AssertNextStep(quote, "TrialConversion", "PurchasePaid");
        Assert.Equal(extendedEnd, quote.GetProperty("validUntil").GetDateTimeOffset());
        Assert.Equal(quote.GetRawText(), (await QuoteAsync(package, trial.Term, request)).GetRawText());
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes"));
    }

    [Fact]
    public async Task ConvertedTrialRefusesFreshQuotes()
    {
        await using var fixture = await P0308Fixture.StartAsync(database, January31);
        var package = await fixture.PackageAsync(1, [ShopNames[0]]);
        var trial = await StartTrialAsync(fixture, package);
        var target = await PublishAsync(fixture, 12000L, members: 100);
        fixture.At(January31.AddDays(1));
        await PurchaseAsync(package, "Month");
        Assert.Equal(1L, await fixture.CountAsync("payment_trial_conversions"));

        var request = Request(target, 3, 1, trial.End);
        for (var delivery = 0; delivery < 2; delivery++)
            await AssertRefusedAsync(package, trial.Term, request, HttpStatusCode.Conflict, "Billing.CapacityChangeSourceChanged");
        Assert.Equal(0L, await fixture.CountAsync("capacity_change_quotes"));
    }

    [Fact]
    public async Task UnrecognizedPaidAccessPolicyNeedsReview()
    {
        await using var fixture = await P0308Fixture.StartAsync(database, January31);
        var (package, term) = await PackageWithRecordedTermAsync(fixture, "future-access-policy/2");
        await AssertRefusedAsync(package, term, Request(package.Plan, 4, 3, February29),
            HttpStatusCode.Conflict, "Billing.CapacityChangeNeedsReview");
        Assert.Equal(0L, await fixture.CountAsync("capacity_change_quotes"));
    }

    [Theory]
    [InlineData(12000L, "AtRenewalOrCourtesy")]
    [InlineData(30000L, "Payable")]
    public async Task MidTermQuoteValidityEndsAtThePaidThroughBoundary(long targetGross, string outcome)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, January31);
        var package = await fixture.PackageAsync(1, [ShopNames[0]]);
        var (_, term) = await PurchaseAsync(package, "Month");
        var target = await PublishAsync(fixture, targetGross, members: 10);
        var at = February29.AddMinutes(-5);
        fixture.At(at);

        var quote = await QuoteAsync(package, term, Request(target, 3, 3, February29));
        Assert.Equal(outcome, quote.GetProperty("outcome").GetString());
        Assert.Equal(February29, quote.GetProperty("validUntil").GetDateTimeOffset());
        Assert.Equal(at.AddMinutes(15), quote.GetProperty("expiresAt").GetDateTimeOffset());
    }

    private static async Task<(Guid Term, DateTimeOffset End)> StartTrialAsync(P0308Fixture fixture, Package package)
    {
        await P0309Tests.ApplyTrialAsync(fixture, package.Payer);
        var term = (Guid)(await fixture.ScalarAsync(
            $"SELECT term_id FROM trial_history WHERE shop_id = '{package.Payer.ShopId:D}'"))!;
        var status = await AccountTermAsync(package, term);
        return (term, status.GetProperty("paidThrough").GetDateTimeOffset());
    }

    private static async Task ExtendTrialAsync(P0308Fixture fixture, Package package, DateTimeOffset endsAt)
    {
        var manager = fixture.Checkout.Trial.CatalogueManager!;
        var path = $"/api/admin/accounts/{package.Account:D}/shops/{package.Payer.ShopId:D}/trial-extensions";
        const string reason = "Fixture evaluation was delayed";
        const string reviewReference = "FIXTURE-909";
        using var reviewed = await P0212Fixture.PostAsync(manager, path + "/preview", new { endsAt, reason, reviewReference });
        Assert.True(reviewed.IsSuccessStatusCode, await reviewed.Content.ReadAsStringAsync());
        var preview = await reviewed.Content.ReadFromJsonAsync<JsonElement>();
        using var created = await P0212Fixture.PostAsync(manager, path, new
        {
            endsAt, reason, reviewReference, operationKey = Guid.NewGuid(), confirmed = true,
            currentVersion = preview.GetProperty("currentVersion").GetInt64(),
            sourceHash = preview.GetProperty("sourceHash").GetString(),
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task<(Package Package, Guid Term)> PackageWithRecordedTermAsync(P0308Fixture fixture,
        string accessPolicy = GracePolicy.Version)
    {
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial);
        var term = await SeedTermAsync(fixture.Hosts, account, new TermSeed(January31, February29,
            AnchorLocal: "2028-01-31 10:00:00", PricePaise: 12000, Grouped: true, Capacity: 3,
            AccessPolicyVersion: accessPolicy, PlanVersionId: plan));
        // Seed the recorded affiliation independently of the fixture's wall-clock consent flow.
        await AffiliateAsync(fixture.Hosts, account, payer.UserId, payer.ShopId, payer.UserId, term, January31, February29);
        return (new(payer, account, [payer], plan), term);
    }
}
