using System.Data;
using System.Net;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    private const string PendingActivationCode = "Licence.PendingActivation";

    [Fact]
    public async Task SiblingAccountReleaseCannotOpenTradingForAnOverCapacityAssignedShop()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var (shop, sibling) = (p.Shops[2], p.Shops[0]);
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        await delivery.DrainAsync(false);
        var staff = new List<Guid>();
        for (var i = 0; i < 5; i++)
            staff.Add((await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, shop.ShopId)).UserId);

        // Deliveries are signed and judged at the licensing instant; member sessions stay on the session clock.
        var clock = fixture.Hosts.Fixture.Clock;
        var sessionNow = clock.Now;
        clock.Now = February14.AddMinutes(1);

        // An immediate account restriction stays deliverable to the over-capacity shop and leaves its slot unactivated.
        using var security = await P0403Tests.SignInSecurityAsync(delivery);
        var restriction = await P0403Tests.ApplySuspensionAsync(security, p.Account, "Account", null, "Trading", February14);
        await DeliverSingleSnapshotAsync(fixture, await LatestSnapshotAsync(fixture, shop.ShopId), expectOutcome: "Applied");
        Assert.Equal("Licence.TradingRestricted", await FencedTradingAsync(fixture, shop.ShopId));
        Assert.Equal(0L, await ActivationsAsync(fixture, shop.ShopId));

        // The sibling's release advances the shared account head before the target reconciles.
        var releasePath = $"/api/admin/accounts/{p.Account:D}/restrictions/{restriction:D}/release";
        using var released = await P0212Fixture.PostAsync(security, releasePath, await P0404Tests.PreviewReleaseAsync(security, releasePath));
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        await DeliverSingleSnapshotAsync(fixture, await LatestSnapshotAsync(fixture, sibling.ShopId), expectOutcome: "Applied");
        Assert.Equal("Unrestricted", await delivery.MerchantScalarAsync(
            $"SELECT restriction FROM account_license_restrictions WHERE account_id='{p.Account}'"));
        Assert.Equal("Allowed", await FencedTradingAsync(fixture, sibling.ShopId));
        Assert.Equal(PendingActivationCode, await FencedTradingAsync(fixture, shop.ShopId));

        // The target's own release still fails its live seat check and cannot open trading.
        await DeliverSingleSnapshotAsync(fixture, await LatestSnapshotAsync(fixture, shop.ShopId), expectOutcome: "NeedsReview");
        Assert.Equal(PendingActivationCode, await FencedTradingAsync(fixture, shop.ShopId));
        Assert.Equal(0L, await ActivationsAsync(fixture, shop.ShopId));

        // Recovery at a valid seat count activates once; exact redelivery replays the same receipt.
        clock.Now = sessionNow;
        await SuspendOneStaffAsync(shop, staff[0]);
        clock.Now = February14.AddMinutes(1);
        await P0403Tests.QueueReconciliationAsync(delivery, shop.ShopId);
        var recovered = await LatestSnapshotAsync(fixture, shop.ShopId);
        await DeliverSingleSnapshotAsync(fixture, recovered, expectOutcome: "Applied");
        Assert.Equal("Allowed", await FencedTradingAsync(fixture, shop.ShopId));
        await DeliverSingleSnapshotAsync(fixture, recovered, expectOutcome: "Applied");
        Assert.Equal(1L, await ActivationsAsync(fixture, shop.ShopId));
        Assert.Equal(assignment, (Guid)(await delivery.MerchantScalarAsync(
            $"SELECT assignment_id FROM coverage_slot_activations WHERE shop_id='{shop.ShopId}'"))!);
        Assert.Equal("Allowed", await FencedTradingAsync(fixture, shop.ShopId));
    }

    private static async Task<Guid> LatestSnapshotAsync(P0308Fixture fixture, Guid shop) => (Guid)(await fixture.ScalarAsync(
        $"SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop}' ORDER BY shop_revision DESC LIMIT 1"))!;

    private static async Task<long> ActivationsAsync(P0308Fixture fixture, Guid shop) => (long)(await fixture.Checkout.Delivery
        .MerchantScalarAsync($"SELECT count(*) FROM coverage_slot_activations WHERE shop_id='{shop}'"))!;

    // The shop's real fenced NewTrading decision on the merchant decision clock.
    private static async Task<string?> FencedTradingAsync(P0308Fixture fixture, Guid shop)
    {
        await using var scope = fixture.Hosts.Merchant.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.active_shop_id', {shop.ToString("D")}, true)");
        var lease = await scope.ServiceProvider.GetRequiredService<IShopLicenceFence>()
            .EnterAsync(shop, LicenceAction.NewTrading, CancellationToken.None);
        Assert.False(lease.IsError, lease.IsError ? lease.FirstError.Code : null);
        var decision = lease.Value.Decision;
        return decision.Outcome == LicenceOutcome.Allowed ? "Allowed" : decision.Denial?.Code;
    }
}
