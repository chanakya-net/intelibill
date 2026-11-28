using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstInsertUsesLifecycleAtTheCommitFence(bool convert)
    {
        var initial = Utc("2028-02-01T04:30:00Z");
        var barrier = new ObservationCommitGapBarrier();
        await using var paid = await P0308Fixture.StartAsync(database, initial,
            services: services => WrapAuthority(services, inner => new BarrierNoticeAuthority(inner, barrier)));
        var shop = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(shop.Client);
        await P0213Fixture.LinkAsync(account, shop, shop);
        var trial = paid.Checkout.Trial;
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shop, trial.Accept())).Status);
        var plan = await P0301Tests.PublishPlanAsync(trial);
        using var owner = await trial.Catalogue.SignInOwnerAsync();
        using var security = await SignInAdminWithAsync(trial.Catalogue, owner, "race@example.test", "security-administrator");
        barrier.ArmNext();
        var pending = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        await barrier.EnteredHttp.Task.WaitAsync(TimeSpan.FromSeconds(15));
        paid.At(initial.AddHours(1));
        Guid? restriction = null;
        try
        {
            if (convert)
                await PurchaseAsync(new Package(shop, account, [shop], plan), "Month");
            else
                restriction = await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{account:D}/restrictions", "Account", null);
        }
        finally
        {
            barrier.ReleaseHttp.TrySetResult();
        }
        Assert.Equal(0, (await pending).Inserted);
        Assert.Equal(0L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, account));
        Assert.True((await RunPlannerPassAsync(paid.Hosts.Licensing.Services)).Inserted > 0);
        if (restriction is { } id)
            await AssertTrialSuspensionReleaseAsync(paid, shop, account, security, id);
    }

    private static async Task AssertTrialSuspensionReleaseAsync(
        P0308Fixture paid, Merchant shop, Guid account, P0110CookieBrowser security, Guid restriction)
    {
        paid.At(Utc("2028-02-12T04:30:00Z"));
        var count = await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, account);
        await AssertRestrictionGuidanceAsync(shop, shop, account, "SuspendedNoPaymentLift", true);
        await ReleaseRestrictionAsync(security, account, restriction);
        await AssertRestrictionGuidanceAsync(shop, shop, account, "TrialEnding", false);
        Assert.Equal(count, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, account));
    }

    private static async Task AssertRestrictionGuidanceAsync(
        Merchant payer, Merchant shop, Guid account, string expected, bool restricted)
    {
        var billing = await payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{account:D}/notices");
        var item = Assert.Single(billing!.Notices);
        Assert.Equal(expected, item.GuidanceCode);
        Assert.Equal(restricted, item.PaymentDoesNotLiftSuspension);
        Assert.Equal(restricted, item.TemplateKey.StartsWith("notice.suspended.", StringComparison.Ordinal));
        var status = await shop.Client.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shop.ShopId:D}/notices");
        var operational = Assert.Single(status!.Notices);
        Assert.Equal(expected, operational.GuidanceCode);
        Assert.Equal(item.TemplateKey, operational.TemplateKey);
    }

    [Fact]
    public async Task BusyFirstAccountDoesNotStarveLaterAccountAndRecoversOnRetry()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Utc("2028-01-31T04:30:00Z"));
        var first = await paid.PackageAsync(1, ["First"]);
        var second = await paid.PackageAsync(1, ["Second"]);
        foreach (var package in new[] { first, second })
            await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        var accounts = new[] { first.Account, second.Account }.Order().ToArray();
        var store = paid.Hosts.LicensingOwner;
        await using var gate = new NpgsqlConnection(store.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", gate, tx);
        cmd.Parameters.AddWithValue("b" + accounts[0].ToString("D"));
        await cmd.ExecuteNonQueryAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var pass = await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            Assert.Equal(1, pass.AccountsSkippedUnavailable);
            Assert.Equal(0L, await CountJobsForAccountAsync(store, accounts[0]));
            Assert.Equal(10L, await CountJobsForAccountAsync(store, accounts[1]));
        }
        await tx.RollbackAsync();
        Assert.Equal(10, (await RunPlannerPassAsync(paid.Hosts.Licensing.Services)).Inserted);
        Assert.Equal(0, (await RunPlannerPassAsync(paid.Hosts.Licensing.Services)).Inserted);
    }

    [Fact]
    public async Task JoinedShopReceivesOnlyItsOwnEffectiveTermThresholds()
    {
        await using var paid = await P0308Fixture.StartAsync(database, P0601Tests.January31);
        var (package, term) = await P0604Tests.PrepareAsync(paid);
        var joined = package.Shops[2];
        await P0604Tests.AcceptSlotAsync(package, term, joined);
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var due = Utc("2028-02-22T04:30:00Z");
        await AssertShopTermNoticeAsync(paid, package.Account, joined, term, due);
    }

    private static async Task AssertEndExclusiveAsync(
        P0201Clock clock, Merchant payer, Merchant shop, Guid accountId, DateTimeOffset deadline, P0110CookieBrowser admin)
    {
        var history = await ReadPreviewIdsAsync(admin, accountId);
        Assert.NotEmpty(history);
        foreach (var ticks in new[] { -10, 0, 10 })
        {
            clock.Now = deadline.AddTicks(ticks);
            var account = await payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{accountId:D}/notices");
            var operational = await shop.Client.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shop.ShopId:D}/notices");
            if (ticks < 0)
            {
                Assert.Equal("Active", Assert.Single(account!.Notices).AccessState);
                Assert.Equal("Active", Assert.Single(operational!.Notices).AccessState);
                Assert.Equal(deadline, account.NextDueAt);
                Assert.Equal(deadline, operational.NextDueAt);
            }
            else
            {
                Assert.Empty(account!.Notices);
                Assert.Empty(operational!.Notices);
            }
        }
        Assert.Equal(history, await ReadPreviewIdsAsync(admin, accountId));
    }

    private static async Task<Guid[]> ReadPreviewIdsAsync(P0110CookieBrowser admin, Guid account)
    {
        using var response = await admin.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/notifications");
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<ProviderNotificationsPageV1>();
        Assert.Null(preview!.NextCursor);
        return preview.Items.Select(x => x.JobId).ToArray();
    }

    private static async Task AssertSuccessorNoticeAsync(P0308Fixture paid, P0308Fixture.Package package, Guid successor)
    {
        await AssertShopTermNoticeAsync(paid, package.Account, package.Shops[0], successor, Utc("2028-03-24T04:30:00Z"));
    }

    private static async Task AssertShopTermNoticeAsync(P0308Fixture paid, Guid account, Merchant shop, Guid term, DateTimeOffset due)
    {
        var rows = await ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner, account, LicenseNoticesV1.ScopeShop, $"user:{shop.UserId:D}", term);
        Assert.Contains((LicenseNoticesV1.ThresholdPaidMinus7Days, due), rows);
        var path = $"/api/licensing/shops/{shop.ShopId:D}/notices";
        paid.At(due.AddTicks(-10));
        Assert.Equal(due, (await shop.Client.GetFromJsonAsync<ShopLicenseNoticesV1>(path))!.NextDueAt);
        paid.At(due);
        Assert.Equal(LicenseNoticesV1.ThresholdPaidMinus7Days,
            Assert.Single((await shop.Client.GetFromJsonAsync<ShopLicenseNoticesV1>(path))!.Notices).Threshold);
    }
}
