using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0703Tests
{
    [Fact]
    public async Task SupersededReminderNeverReachesSmtpAfterTrialConversion()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(1);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(package.Payer, paid.Checkout.Trial.Accept())).Status);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Start.AddDays(11));
        await PurchaseAsync(package, "Month");
        await DrainMailAsync(paid);
        Assert.Equal("Suppressed", await StateAsync(paid, package, "TrialMinus3Days"));
        Assert.Empty(await mailpit.ReadAsync(package.Payer.Email));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SupersededReminderNeverReachesSmtpForClosedScope(bool wholeAccount)
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(2);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Due);
        var other = package.Shops[1];
        if (wholeAccount)
        {
            var proposal = await P0416Tests.ProposeAsync(package.Payer, package.Account);
            foreach (var shop in package.Shops)
                await P0416Tests.ConsentAsync(shop, proposal.GetProperty("id").GetGuid());
            using var confirmation = await P0416Tests.ConfirmAccountAsync(package.Payer, package.Account, proposal);
            confirmation.EnsureSuccessStatusCode();
        }
        else
            await P0417Tests.CloseShopAsync(other, package.Account);
        await DrainMailAsync(paid);
        Assert.Equal(wholeAccount ? "Suppressed" : "SmtpAccepted", await StateAsync(paid, package));
        Assert.Equal("Suppressed", await paid.ScalarAsync($"""
            SELECT d.state FROM notification_email_deliveries d JOIN notification_jobs j ON j.id = d.job_id
            WHERE j.scope_id = '{other.ShopId:D}' AND j.recipient_user_id = '{other.UserId:D}' AND j.threshold = 'PaidMinus7Days'
            """));
        Assert.Empty(await mailpit.ReadAsync(other.Email));
        Assert.Equal(wholeAccount ? 0 : 1, (await mailpit.ReadAsync(package.Payer.Email)).Length);
    }

    [Fact]
    public async Task SupersededReminderNeverReachesSmtpForReplacedTermOnly()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var all = await paid.PackageAsync(2);
        var first = all with { Shops = [all.Shops[0]] };
        var second = all with { Shops = [all.Shops[1]] };
        var (_, term) = await PurchaseAsync(first, "Month");
        var (_, unaffected) = await PurchaseAsync(second, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var plan = (Guid)(await paid.ScalarAsync($"SELECT plan_id FROM plan_versions WHERE id = '{first.Plan:D}'"))!;
        var successor = await P0301Tests.PublishPlanAsync(paid.Checkout.Trial, plan, first.Plan, membersPerShop: 7);
        paid.At(Due);
        var renewed = await RenewAsync(first with { Plan = successor }, term, "Month");
        P0302Fixture.AssertStatus(renewed.Status, "Verified", "Fulfilled", "Applying");
        await DrainMailAsync(paid);
        Assert.Equal("Suppressed", await AccountTermStateAsync(paid, first, term));
        Assert.Equal("SmtpAccepted", await AccountTermStateAsync(paid, second, unaffected));
        Assert.Single(await mailpit.ReadAsync(all.Payer.Email));
    }

    [Fact]
    public async Task SupersededReminderNeverReachesSmtpAfterRestoration()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(1);
        var (_, term) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Start.AddMonths(1).AddDays(10));
        var (order, status) = await RenewAsync(package, term, "Month");
        Assert.Equal("RestoredAccess", order.StartRule);
        P0302Fixture.AssertStatus(status, "Verified", "Fulfilled", "Applying");
        await DrainMailAsync(paid);
        Assert.Equal("Suppressed", await StateAsync(paid, package, "GraceExpiry"));
        Assert.Empty(await mailpit.ReadAsync(package.Payer.Email));
    }

    [Fact]
    public async Task SupersededReminderNeverReachesSmtpForShopWithActiveGrant()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var package = await paid.PackageAsync(2);
        var shop = package.Shops[1];
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var expiry = Start.AddMonths(1).AddDays(7);
        await P0701Tests.GrantComplimentaryAsync(
            paid.Checkout.Trial.CatalogueManager!,
            package.Account,
            shop.ShopId,
            expiry,
            expiry.AddDays(10));
        paid.At(expiry);
        await DrainMailAsync(paid);
        Assert.Equal("SmtpAccepted", await StateAsync(paid, package, "GraceExpiry"));
        Assert.Single(await mailpit.ReadAsync(package.Payer.Email));
        Assert.Equal("SourceNoLongerCurrent", await paid.ScalarAsync($"""
            SELECT e.reason FROM notification_email_events e JOIN notification_email_deliveries d ON d.id = e.delivery_id
            JOIN notification_jobs j ON j.id = d.job_id WHERE j.scope_id = '{shop.ShopId:D}'
                AND j.threshold = 'GraceExpiry' AND e.state = 'Suppressed'
            """));
        Assert.Empty(await mailpit.ReadAsync(shop.Email));
    }

    [Fact]
    public async Task SupersededReminderNeverReachesSmtpAfterShopTransfer()
    {
        using var key = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var scenario = await P0607aTests.StartAsync(database, key, licensingSettings: Settings(mailpit));
        var paid = scenario.F;
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var job = (Guid)(await paid.ScalarAsync($"""
            SELECT d.id FROM notification_email_deliveries d JOIN notification_jobs j ON d.job_id = j.id
            WHERE j.scope_id = '{scenario.North.ShopId:D}' AND j.source_id = '{scenario.Term:D}'
                AND j.recipient_user_id = '{scenario.North.UserId:D}' AND j.threshold = 'PaidMinus7Days' AND d.state = 'Pending'
            """))!;
        await P0607Tests.ApplyTransferAsync(scenario);
        paid.At(Due);
        await DrainMailAsync(paid);
        Assert.Equal("Suppressed", await paid.ScalarAsync($"SELECT state FROM notification_email_deliveries WHERE id = '{job:D}'"));
        Assert.Empty(await mailpit.ReadAsync(scenario.North.Email));
        Assert.Single(await mailpit.ReadAsync(scenario.Source.Payer.Email));
    }

    private static Task<object?> AccountTermStateAsync(P0308Fixture paid, Package package, Guid term) =>
        paid.ScalarAsync($"""
            SELECT d.state FROM notification_email_deliveries d JOIN notification_jobs j ON j.id = d.job_id
            WHERE j.account_id = '{package.Account:D}' AND j.scope_kind = 'Account' AND j.source_id = '{term:D}'
                AND j.threshold = 'PaidMinus7Days'
            """);

    [Fact]
    public async Task DeliversOnlyTheCurrentThresholdAndGuidanceForNegotiatedAgreement()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var payer = await SignInAsync(paid.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var version = await PublishCustomVersionAsync(paid);
        var local = paid.Hosts.Fixture.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(5.5));
        var start = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
        var end = start.AddMonths(1).ToUniversalTime();
        var term = await SeedTermAsync(paid.Hosts, account, new TermSeed(start.ToUniversalTime(), end,
            AnchorLocal: FormattableString.Invariant($"{start:yyyy-MM-dd HH:mm:ss}"), PricePaise: 190000, PlanVersionId: version));
        paid.At(start.ToUniversalTime());
        await P0213Fixture.LinkAsync(account, payer, payer, term);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(end.AddDays(-7).AddHours(4));
        await DrainMailAsync(paid);
        var message = Assert.Single(await mailpit.ReadAsync(payer.Email));
        Assert.Contains($"IST ({end:O} UTC)", message);
        Assert.Contains("1,900.00", message);
        Assert.DoesNotContain("7 days before", message);
        Assert.DoesNotContain("Negotiation evidence", message);
    }

    private static async Task<Guid> PublishCustomVersionAsync(P0308Fixture paid)
    {
        var trial = paid.Checkout.Trial;
        var facts = P0207Tests.PlanFacts(0);
        facts["kind"] = "custom";
        facts["name"] = "Negotiated monthly agreement";
        facts["reason"] = "Negotiation evidence";
        facts["monthlyPricePaise"] = 190000L;
        facts["extraShopsOffered"] = false;
        facts["extraShopMonthlyPricePaise"] = null;
        facts["extraShopAnnualPricePaise"] = null;
        var plan = Guid.NewGuid();
        var manager = trial.CatalogueManager!;
        var draft = await P0207Tests.SavePlanAsync(manager, plan, facts, HttpStatusCode.Created);
        var version = draft.GetProperty("versionId").GetGuid();
        var path = $"/api/admin/plans/{plan:D}/versions/{version:D}";
        var preview = await P0207Tests.PrepareAsync(manager, path, Guid.NewGuid(), 1, trial.PolicyId);
        using var published = await P0207Tests.ConfirmAsync(manager, path, preview);
        published.EnsureSuccessStatusCode();
        return version;
    }
}
