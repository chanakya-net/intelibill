using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task CourtesyBridgeConservesThePurchasedSlotAtEveryBoundary()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var originalHistory = await P0605Tests.OriginalHistoryDigestAsync(f, p, term);

        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;
        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", cutover);

        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("conservation.manager@example.test");
        using var _ = manager;
        var preview = await AdminPreviewAsync(manager, p.Account, id);
        using var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(), preview,
            "Approved mid-term branch handover with auditable courtesy bridge");
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);

        await DrainExceptionProtocolAsync(f);

        // Separate auditable courtesy grant created and linked; original paid order history untouched.
        Assert.Equal(1L, await f.CountAsync("paid_slot_handoffs", $"exception_id='{id}' AND courtesy_grant_id IS NOT NULL"));
        Assert.Equal(1L, await f.CountAsync("complimentary_grants", $"account_id='{p.Account}' AND starts_at='{EarlyStart:O}' AND ends_at='{cutover:O}'"));
        Assert.Equal(1L, await f.CountAsync("complimentary_grant_receipts", $"account_id='{p.Account}'"));
        Assert.Equal(originalHistory, await P0605Tests.OriginalHistoryDigestAsync(f, p, term));

        // During [C, B): North still trades on Paid; South trades on Complimentary (courtesy bridge); siblings unaffected.
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[2].ShopId, EarlyStart));
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[3].ShopId, EarlyStart));
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[0].ShopId, EarlyStart));
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[1].ShopId, EarlyStart));

        var previewAtC = await AdminPreviewAsync(manager, p.Account, id);
        var approvalAtC = previewAtC.GetProperty("approval");
        Assert.Equal("Applied", approvalAtC.GetProperty("state").GetString());
        Assert.Equal("Acknowledged", approvalAtC.GetProperty("outgoingActivation").GetString());
        Assert.Equal("Acknowledged", approvalAtC.GetProperty("courtesyActivation").GetString());
        Assert.Equal("Waiting", approvalAtC.GetProperty("incomingActivation").GetString());

        // One microsecond before cutover B: North still Allowed, South still Allowed (courtesy).
        await P0605Tests.VerifyTradingAsync(f, p, cutover.AddTicks(-10), "Allowed", "Allowed");
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_activations WHERE exception_id='{id}' AND leg='IncomingCourtesy'"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_activations WHERE exception_id='{id}' AND leg='IncomingPaid'"));

        // Exact cutover B: North blocked with Licence.Expired (no grace), South switches to Paid (IncomingPaid activated).
        await P0605Tests.VerifyTradingAsync(f, p, cutover, "Licence.Expired", "Allowed", deliverReceipts: true);
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_activations WHERE exception_id='{id}' AND leg='IncomingPaid'"));

        // North's history access remains allowed after cutover while new trading is blocked.
        Assert.Equal("Allowed", await FencedHistoryAtAsync(f, p.Shops[2].ShopId, cutover));
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[0].ShopId, cutover));
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[1].ShopId, cutover));

        var previewAtB = await AdminPreviewAsync(manager, p.Account, id);
        var approvalAtB = previewAtB.GetProperty("approval");
        Assert.Equal("Acknowledged", approvalAtB.GetProperty("outgoingActivation").GetString());
        Assert.Equal("Acknowledged", approvalAtB.GetProperty("incomingActivation").GetString());
        Assert.Equal("TermEnded", approvalAtB.GetProperty("courtesyActivation").GetString());

        // Independent SQL interval conservation oracle: disjoint, adjacent, exact total paid duration, holders <= capacity (3).
        await AssertSlotConservationOracleAsync(f, p.Account, term, capacity: 3, expectedStart: January31, expectedEnd: February29);

        // Ordinary renewal uses effective roster {Central, East, South}, never North.
        f.At(cutover.AddHours(1));
        var renewalQuote = await RenewalQuoteAsync(p, term, "Month");
        Assert.Equal(3, renewalQuote.ShopCount);
        Assert.Equal(CheckoutOrdersV1.ShopHash([p.Shops[0].ShopId, p.Shops[1].ShopId, p.Shops[3].ShopId]), renewalQuote.ShopHash);
        var renewed = await PayAsync(p, await AcceptAsync(p, renewalQuote));
        var successorTerm = renewed.GetProperty("termId").GetGuid();
        Assert.Equal(1L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{successorTerm}' AND shop_id='{p.Shops[3].ShopId}'"));
        Assert.Equal(0L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{successorTerm}' AND shop_id='{p.Shops[2].ShopId}'"));
        Assert.Equal(originalHistory, await P0605Tests.OriginalHistoryDigestAsync(f, p, term));
    }

    [Fact]
    public async Task WaitDispositionKeepsIncomingBlockedUntilCutoverWithoutCourtesyGrant()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;

        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "Wait", cutover);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("wait.manager@example.test");
        using var _ = manager;
        var preview = await AdminPreviewAsync(manager, p.Account, id);
        using var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(), preview,
            "Approved wait-until-permit-end handoff with zero overlap");
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        await DrainExceptionProtocolAsync(f);

        Assert.Equal(0L, await f.CountAsync("complimentary_grants", $"account_id='{p.Account}'"));
        Assert.Equal(1L, await f.CountAsync("paid_slot_handoffs", $"exception_id='{id}' AND courtesy_grant_id IS NULL"));

        // Before cutover B: North trades, South stays PendingActivation (Scheduled), no courtesy grant.
        await P0605Tests.VerifyTradingAsync(f, p, cutover.AddTicks(-10), "Allowed", "Licence.PendingActivation");
        // At cutover B: North expires, South activates Paid.
        await P0605Tests.VerifyTradingAsync(f, p, cutover, "Licence.Expired", "Allowed", deliverReceipts: true);
        await AssertSlotConservationOracleAsync(f, p.Account, term, capacity: 3, expectedStart: January31, expectedEnd: February29);
    }

    private static async Task<string?> FencedHistoryAtAsync(P0308Fixture f, Guid shopId, DateTimeOffset at)
    {
        var clock = f.Hosts.Fixture.Clock;
        var original = clock.Now;
        try
        {
            clock.Now = at;
            using var scope = f.Hosts.Merchant.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Intelibill.Infrastructure.Data.ApplicationDbContext>();
            var fence = scope.ServiceProvider.GetRequiredService<Intelibill.Application.Common.Licensing.IShopLicenceFence>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlInterpolatedAsync(
                db.Database, $"SELECT set_config('app.active_shop_id', {shopId.ToString("D")}, true)");
            var res = await fence.EnterAsync(shopId, Intelibill.Application.Common.Licensing.LicenceAction.History, CancellationToken.None);
            return res.IsError ? res.FirstError.Code : res.Value.Decision.Denial?.Code ?? "Allowed";
        }
        finally { clock.Now = original; }
    }
}
