using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    internal static async Task<string> OriginalHistoryDigestAsync(P0308Fixture f, P0308Fixture.Package p, Guid term)
    {
        var order = (Guid)(await f.ScalarAsync($"SELECT order_id FROM payment_fulfilments WHERE term_id='{term}'"))!;
        var hashes = new List<string>();
        foreach (var table in new[] { "shop_affiliations", "shop_affiliation_requests", "payment_fulfilment_shops", "checkout_order_shops",
            "payment_fulfilments", "verified_payments", "checkout_orders", "payment_confirmations", "subscription_term_facts" })
        {
            var predicate = table.StartsWith("shop_", StringComparison.Ordinal) ? $"account_id='{p.Account}'" :
                table == "subscription_term_facts" ? $"term_id='{term}'" : $"order_id='{order}'";
            hashes.Add((string)(await f.ScalarAsync($"""
                SELECT encode(sha256(convert_to(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t))::text,'UTF8')),'hex')
                FROM public.{table} t WHERE {predicate}
                """))!);
        }
        return string.Join("|", hashes);
    }

    [Fact]
    public async Task SingleShopRenewalMovesOnlyTheSelectedSuccessorAndKeepsTheOriginalOrder()
    {
        await using var f = await StartAsync(database, January31);
        var p = await f.PackageAsync(4);
        p = p with { Plan = await P0603Tests.PublishReductionPlanAsync(f, "single-shop", "Replacement single", 1, 5, 6000) };
        var (_, term) = await PurchaseAsync(p with { Shops = [p.Shops[2]] }, "Month");
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        f.At(February14);
        var original = await OriginalHistoryDigestAsync(f, p, term);
        await ConsentAsync(f, p, term, capacity: 1);
        var (quote, paid) = await RenewAsync(p, term, "Month");
        Assert.Equal(1, quote.ShopCount);
        Assert.Equal(Intelibill.Shared.Contracts.V1.CheckoutOrdersV1.ShopHash([p.Shops[3].ShopId]), quote.ShopHash);
        Assert.Equal(1L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{paid.GetProperty("termId").GetGuid()}' AND shop_id='{p.Shops[3].ShopId}'"));
        Assert.Equal(original, await OriginalHistoryDigestAsync(f, p, term));
    }

    [Fact]
    public async Task ReplacementCannotExposeAnotherAccountsHistoryOrReplayAfterDemotion()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var path = PathFor(p, term);
        var key = Guid.NewGuid();
        var command = Request(p, key);
        using var created = await p.Payer.Client.PostAsJsonAsync(path, command);
        created.EnsureSuccessStatusCode();
        using var foreign = await p.Shops[1].Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
            VALUES(gen_random_uuid(),'{p.Account}','{p.Shops[1].UserId}',2,1,1,now());
            UPDATE billing_account_members SET role=CASE WHEN merchant_user_id='{p.Payer.UserId}' THEN 2 ELSE 1 END
            WHERE account_id='{p.Account}' AND merchant_user_id IN ('{p.Payer.UserId}','{p.Shops[1].UserId}')
            """);
        using var replay = await p.Payer.Client.PostAsJsonAsync(path, command);
        using var recovery = await p.Payer.Client.GetAsync(path + "/operations/" + key);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, recovery.StatusCode);
        var stale = await p.Payer.Client.GetFromJsonAsync<Intelibill.Shared.Contracts.V1.CoverageReplacementPreviewV1>(path);
        Assert.Equal("Blocked", Assert.Single(stale!.Replacements).State);
        using var fresh = await p.Shops[1].Client.PostAsJsonAsync(path, Request(p, Guid.NewGuid()));
        Assert.True(fresh.IsSuccessStatusCode, await fresh.Content.ReadAsStringAsync());
        Assert.Equal(1L, await f.CountAsync("coverage_replacements", "state='Blocked'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacements", "state='AwaitingConsent'"));
    }

    [Fact]
    public async Task BothDatabasesRefuseDowngradeThatWouldDiscardReplacementEvidence()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (id, _) = await ConsentAsync(f, p, term);
        await using var licensing = f.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = P0209Fixture.MerchantDb(database);
        var original = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            licensing.GetService<IMigrator>().MigrateAsync("20261125120000_CoverageSlotAssignments"));
        var consent = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            merchant.GetService<IMigrator>().MigrateAsync("20261124150000_AddMerchantCoverageSlotConsents"));
        Assert.Equal("55000", Assert.IsType<PostgresException>(original.GetBaseException()).SqlState);
        Assert.Equal("55000", Assert.IsType<PostgresException>(consent.GetBaseException()).SqlState);
        Assert.Equal(1L, await f.CountAsync("coverage_replacements", $"replacement_id='{id}'"));
        Assert.Equal(2L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_consent_events WHERE replacement_id='{id}'"));
    }
}
