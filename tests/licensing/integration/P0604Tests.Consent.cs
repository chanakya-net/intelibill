using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    internal static string Consent(Package p, Guid shop, Guid assignment) =>
        $"/api/billing/accounts/{p.Account}/slot-assignments/{shop}/{assignment}/consent";

    internal static async Task<Guid> BeginAsync(Package p, Guid term, Guid shop)
    {
        using var response = await p.Payer.Client.PostAsJsonAsync(Slots(p, term), Request(shop, Guid.NewGuid()));
        Assert.True(response.IsSuccessStatusCode, $"Slot request returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
    }

    internal static Task<HttpResponseMessage> Decide(HttpClient client, string consent, string decision) =>
        client.PostAsJsonAsync($"{consent}/{decision}", new { operationKey = Guid.NewGuid() });

    internal static async Task<Guid> AcceptSlotAsync(Package p, Guid term, P0209Fixture.Merchant shop)
    {
        var id = await BeginAsync(p, term, shop.ShopId);
        var path = Consent(p, shop.ShopId, id);
        using var forbidden = await Decide(p.Payer.Client, path, "grant");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var granted = await Decide(shop.Client, path, "grant");
        granted.EnsureSuccessStatusCode();
        using var accepted = await Decide(shop.Client, path, "accept");
        accepted.EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task FinalSlotReceiptsCompeteForOneRemainingTermAndGrantNoShopMembership()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var delivery = fixture.Checkout.Delivery;
        await Task.WhenAll(p.Shops.Skip(2).Select(shop => AcceptSlotAsync(p, term, shop)));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        var receipts = await ReadConsentBytesAsync(fixture, p);
        await DeliverUnderContentionAsync(fixture, p.Account, receipts);
        Assert.Equal(1L, await fixture.CountAsync("coverage_slot_assignments", "state='Applied'"));
        Assert.Equal(1L, await fixture.CountAsync("coverage_slot_assignments", "reason='SlotUnavailable'"));
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        var id = (Guid)(await fixture.ScalarAsync("SELECT shop_id FROM coverage_slot_assignments WHERE state='Applied'"))!;
        var shop = p.Shops.Single(s => s.ShopId == id);
        var facts = await ShopTermAsync(p, shop);
        Assert.Equal(February29, facts.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(1L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        using var refused = await p.Payer.Client.PostAsJsonAsync("/api/shops/switch", new { shopId = id });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(0L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE user_id='{p.Payer.UserId}' AND shop_id='{id}'"));
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.Equal(1L, await fixture.CountAsync("coverage_slot_assignment_deliveries"));
    }

    private static async Task DeliverUnderContentionAsync(P0308Fixture f, Guid account, byte[][] receipts)
    {
        await using var connection = new NpgsqlConnection(f.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        await using var gate = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1,0))", connection, gate);
        hold.Parameters.AddWithValue("b" + account.ToString("D"));
        await hold.ExecuteNonQueryAsync();
        var pending = Task.WhenAll(receipts.Select(bytes => DeliverConsentAsync(f.Checkout.Delivery, bytes)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while ((long)(await f.ScalarAsync("""
            SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory'
              AND cardinality(pg_blocking_pids(pid))>0
            """))! < 2) await Task.Delay(10, deadline.Token);
        await gate.CommitAsync();
        foreach (var response in await pending)
        {
            using (response) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    private static async Task<byte[][]> ReadConsentBytesAsync(P0308Fixture f, Package p)
    {
        var receipts = new List<byte[]>();
        foreach (var shop in p.Shops.Skip(2))
            receipts.Add((byte[])(await f.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT payload FROM entitlement_messages WHERE shop_id='{shop.ShopId}' AND kind='CoverageSlotConsent'"))!);
        return receipts.ToArray();
    }

    private static async Task<HttpResponseMessage> DeliverConsentAsync(P0213Fixture f, byte[] bytes)
    {
        using var signed = await f.Transport.SignedAsync(bytes, f.AuthClock, merchant: true,
            path: "/internal/licensing/v1/merchant-events/coverage-slot-consent");
        return await f.LicensingApi.SendAsync(signed);
    }
}
