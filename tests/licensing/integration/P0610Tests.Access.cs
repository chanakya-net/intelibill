using System.Net;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task RetainedApprovalKeepsEffectiveAccessWithoutDispatch()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261125100918_RefundApprovals");
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        await migrator.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        var item = await MerchantOutcomeAsync(package);
        Assert.Equal("Historical", item.GetProperty("providerState").GetString());
        Assert.Equal("Applied", item.GetProperty("accessDisposition").GetString());
        var term = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal(Instant.AddDays(3), term.GetProperty("accessEndsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task OutOfOrderSettlementsKeepOneEffectiveBoundary()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 6000);
        var second = await RequestRefundAsync(package, order, confirmation, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, first, Instant.AddDays(4));
        var path = RequestPath(package.Account, first) + "/simulator";
        using var pending = await P0212Fixture.PostAsync(finance, path, new { outcome = "Pending" });
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        await PumpRefundsAsync(fixture);
        await ApproveEndAsync(finance, package.Account, second, Instant.AddDays(3));
        await PumpRefundsAsync(fixture);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        using var done = await P0212Fixture.PostAsync(finance, path, new { outcome = "Processed" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        await PumpRefundsAsync(fixture);
        Assert.Equal(2L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='NeedsReview'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(snapshots, await fixture.CountAsync("entitlement_snapshots"));
        var term = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal(Instant.AddDays(3), term.GetProperty("accessEndsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ConfirmedRefundLeavesReviewedAccessPendingWhenTheTermHasSinceRenewed()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        var original = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        var term = original.GetProperty("termId").GetGuid();
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        await P0308Fixture.RenewAsync(package, term, "Month");
        await P0608Tests.DrainAsync(fixture);
        var renewed = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='NeedsReview'"));
        Assert.Equal(snapshots, await fixture.CountAsync("entitlement_snapshots"));
        var after = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal(renewed.GetProperty("paidThrough").GetDateTimeOffset(), after.GetProperty("paidThrough").GetDateTimeOffset());
    }

    [Fact]
    public async Task HostedWorkerConsumesOneCommittedSend()
    {
        var settings = new Dictionary<string, string?>(P0302Fixture.SimulatorSettings)
        {
            ["Payments:ReconcileInBackground"] = "true",
        };
        await using var fixture = await P0609Tests.StartAsync(database, Instant, settings);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await fixture.CountAsync("refund_work", "state='Confirmed'") == 0)
            await Task.Delay(100, deadline.Token);
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications"));
    }

    private static async Task ApproveEndAsync(P0110CookieBrowser finance, Guid account, Guid request, DateTimeOffset end)
    {
        var preview = await PreviewAsync(finance, account, request, 4500, "EndAt", end);
        await ReadAsync(await ApproveAsync(finance, account, request, Command(preview, 4500, "EndAt", end)),
            HttpStatusCode.Created);
    }
}
