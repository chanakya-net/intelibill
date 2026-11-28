using System.Net;
using System.Security.Cryptography;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public async Task AgreedIntervalsConserveOneFundedSlot()
    {
        using var key = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, key);
        var history = await P0605Tests.OriginalHistoryDigestAsync(s.F, s.Source, s.Term);
        var cutover = s.Permit.ValidUntil;
        var id = await ConsentedAsync(s, cutover);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("allocation@example.test");
        using var session = manager;
        s.F.Hosts.Fixture.Clock.Now = s.F.Checkout.Trial.Catalogue.Clock.GetUtcNow();
        var operation = Guid.NewGuid();
        var path = $"/api/admin/accounts/{s.Source.Account}/coverage-transfers/{id}";
        var csrf = await P0206Fixture.ProofAsync(manager);
        using var preview = await manager.SendAsync(HttpMethod.Post, path + "/preview", new { }, csrf);
        using var decision = await manager.SendAsync(HttpMethod.Post, path + "/approve",
            Approval(await P0206Fixture.BodyAsync(preview), operation), csrf);
        Assert.True(decision.StatusCode == HttpStatusCode.Created, await decision.Content.ReadAsStringAsync());
        await DrainAsync(s);
        await AssertStateAsync(manager, path, operation, "Scheduled");
        s.F.At(cutover.AddTicks(-10));
        await s.F.Checkout.Delivery.ScanAsync(true);
        await AssertStateAsync(manager, path, operation, "Scheduled");
        Assert.Equal(s.Source.Account, await BindingAsync(s));
        s.F.At(cutover);
        s.F.Hosts.Fixture.Clock.Now = cutover;
        s.F.Checkout.Delivery.Transport.LeaseClock.Now = cutover.AddSeconds(1);
        await s.F.Checkout.Delivery.ScanAsync(true);
        await DrainAsync(s);
        await DrainAsync(s);
        await AssertStateAsync(manager, path, operation, "Applied");
        Assert.Equal(s.Destination.Account, await BindingAsync(s));
        Assert.Equal(cutover, s.Permit.ValidUntil);
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, cutover));
        Assert.Equal(history, await P0605Tests.OriginalHistoryDigestAsync(s.F, s.Source, s.Term));
        await VerifyIntervalAsync(s);
        Assert.Equal(3L, await s.F.CountAsync("payment_fulfilment_shops", $"term_id='{s.Term}'"));
        Assert.Equal(1L, await s.F.CountAsync("payment_fulfilment_shops", $"term_id='{s.DestinationTerm}'"));
        Assert.Equal(false, await s.F.ScalarAsync(
            $"SELECT public.capacity_slot_available('{s.Source.Account}','{s.Term}',3)"));
    }

    private static async Task VerifyIntervalAsync(Scenario s)
    {
        Assert.Equal(true, await s.F.ScalarAsync($"""
            SELECT i.interval_starts_at=original.interval_starts_at AND i.interval_ends_at=b.cutover_at
                AND b.paid_through_at=original.interval_ends_at AND b.member_allowance=f.member_allowance
                AND NOT i.is_final_holder
            FROM public.coverage_transfer_bindings b
            JOIN LATERAL public.term_paid_slot_intervals(b.source_account_id,b.source_term_id) i
                ON i.affiliation_request_id=b.predecessor_id
            JOIN LATERAL public.pre_transfer_paid_slot_intervals(b.source_account_id,b.source_term_id) original
                ON original.affiliation_request_id=b.predecessor_id
            JOIN public.subscription_term_facts f ON f.term_id=b.source_term_id AND f.basis='Paid'
            WHERE b.shop_id='{s.North.ShopId}'
            """));
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.Source.Shops[1].ShopId, s.Permit.ValidUntil));
        Assert.NotEqual("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, P0601Tests.February29));
    }

    private static Task<object?> BindingAsync(Scenario s) => s.F.Checkout.Delivery.MerchantScalarAsync(
        $"SELECT account_id FROM shop_license_states WHERE shop_id='{s.North.ShopId}'");

    private static async Task DrainAsync(Scenario s)
    {
        await s.F.Checkout.Delivery.DrainAsync(true);
        await s.F.Checkout.Delivery.DrainAsync(false);
    }

    private static async Task AssertStateAsync(P0110CookieBrowser manager, string path, Guid operation, string state)
    {
        using var result = await manager.SendAsync(HttpMethod.Get, path + $"/operations/{operation}");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(state, (await P0206Fixture.BodyAsync(result)).GetProperty("state").GetString());
    }
}
