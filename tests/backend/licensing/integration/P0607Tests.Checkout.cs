using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public async Task ReceivingOwnerCanPurchaseTheNamedShopOnlyAfterItsCarriedPeriodEnds()
    {
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("checkout@example.test");
        using var session = manager;
        var merchantIdentityClock = s.F.Hosts.Fixture.Clock.Now;
        var key = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        await AdvanceAsync(s, s.Permit.ValidUntil);
        await AssertStateAsync(manager, AdminPath(s, id), key, "Applied");

        var named = s.Destination with { Shops = [s.North] };
        s.F.Hosts.Fixture.Clock.Now = merchantIdentityClock;
        var path = P0301Tests.OrdersPath(named.Account);
        var quote = new CreateCheckoutQuoteV1(Guid.NewGuid(), named.Plan, "Month", [s.North.ShopId], 0, 1);
        using var overlap = await named.Payer.Client.PostAsJsonAsync(path, quote);
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        Assert.Equal(CheckoutOrdersV1.Invalid, await CodeAsync(overlap));

        s.F.At(P0601Tests.February29);
        var (_, term) = await P0308Fixture.PurchaseAsync(named, "Month");
        await AdvanceAsync(s, P0601Tests.February29);
        Assert.NotEqual(s.DestinationTerm, term);
        Assert.Equal(term, await s.F.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT term_id FROM shop_license_states WHERE account_id='{named.Account}' AND shop_id='{s.North.ShopId}'"));
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, P0601Tests.February29));
        Assert.Equal(1L, await s.F.CountAsync("payment_fulfilment_shops",
            $"account_id='{named.Account}' AND shop_id='{s.North.ShopId}' AND term_id='{term}'"));
        Assert.Equal(3L, await s.F.CountAsync("payment_fulfilment_shops", $"term_id='{s.Term}'"));
    }
}
