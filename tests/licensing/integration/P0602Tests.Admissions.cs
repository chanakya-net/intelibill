using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdmissionsAndChangesSerializeAtTheCapacityFence(bool consumeFirst)
    {
        var at = P0202Fixture.WholeSecondNow();
        await using var fixture = await StartAsync(database, at);
        var package = await fixture.PackageAsync(2);
        // The live affiliation worker has committed both owner consents. Freeze purchase and admission at
        // the same (post-consent) second, including the real merchant decision and service-signing clock.
        var decisionAt = P0202Fixture.WholeSecondNow().AddSeconds(1);
        fixture.At(decisionAt);
        fixture.Hosts.Fixture.Clock.Now = decisionAt;
        var (purchase, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        var shop = package.Shops[1]; // Deliberately admit on a covered shop independent of the routing shop.
        var staff = await StaffAsync(fixture, shop.ShopId, consumeFirst ? 4 : 3);
        var order = await IncreaseSeatsAsync(fixture, package, term, purchase.PreviewEndsAt);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb,
            $"UPDATE shop_license_states SET enforcement_activated_at=now() WHERE shop_id='{shop.ShopId}'");
        await PayAsync(package, order);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.Equal(1L, await fixture.CountAsync("entitlement_messages", "kind='CapacityConsume'"));

        await using var gate = new NpgsqlConnection(delivery.MerchantDb.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('a{package.Account:D}',859))", gate, tx);
        await hold.ExecuteNonQueryAsync();
        Task consume;
        Task<HttpResponseMessage> admit;
        if (consumeFirst)
        {
            consume = delivery.DrainAsync(true);
            await WaitForFenceAsync(delivery, 1);
            admit = ActivateAsync(shop, staff[0]);
        }
        else
        {
            admit = ActivateAsync(shop, staff[0]);
            await WaitForFenceAsync(delivery, 1);
            consume = delivery.DrainAsync(true);
        }
        await WaitForFenceAsync(delivery, 2);
        await tx.CommitAsync();
        using var admitted = await admit;
        Assert.True(admitted.IsSuccessStatusCode, await admitted.Content.ReadAsStringAsync());
        await consume;
        await delivery.DrainAsync(false);
        using var excess = await ActivateAsync(shop, staff[1]);
        Assert.Equal(HttpStatusCode.Conflict, excess.StatusCode);
        Assert.Equal("Licence.MemberCapacityReached", await P0206Fixture.CodeAsync(excess));
        var paid = await P0302Fixture.StatusAsync(package.Payer.Client, order);
        P0302Fixture.AssertStatus(paid, "Verified", consumeFirst ? "Fulfilled" : "NeedsReview", consumeFirst ? "Applied" : "NotApplicable");
        Assert.Equal(consumeFirst ? 6L : 5L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE shop_id='{shop.ShopId}' AND status='Active'"));
    }

    private static Task<HttpResponseMessage> ActivateAsync(P0209Fixture.Merchant owner, Guid staff) =>
        owner.Client.PatchAsJsonAsync($"/api/shops/{owner.ShopId}/members/{staff}", new { role = "Staff", status = "Active", expectedVersion = 1 });

    private static async Task<Guid[]> StaffAsync(P0308Fixture fixture, Guid shop, int active)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < active + 2; i++)
        {
            var staff = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, shop);
            if (i < active) continue;
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Checkout.Delivery.MerchantDb,
                $"UPDATE shop_memberships SET status='Suspended', authority_version=1 WHERE shop_id='{shop}' AND user_id='{staff.UserId}'");
            ids.Add(staff.UserId);
        }
        return [.. ids];
    }

    private static async Task<CheckoutQuoteV1> IncreaseSeatsAsync(P0308Fixture fixture, Package package, Guid term, DateTimeOffset end, int capacity = 4)
    {
        var plan = (Guid)(await fixture.ScalarAsync($"SELECT plan_id FROM plan_versions WHERE id='{package.Plan}'"))!;
        var target = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial, plan, package.Plan, 6, 18000);
        using var preview = await PostQuoteAsync(package, term, Request(target, capacity, 3, end));
        preview.EnsureSuccessStatusCode();
        var q = await preview.Content.ReadFromJsonAsync<JsonElement>();
        using var converted = await package.Payer.Client.PostAsJsonAsync(
            $"{QuotesPath(package.Account, term)}/{q.GetProperty("quoteId").GetGuid()}/order",
            new { operationKey = Guid.NewGuid(), snapshotHash = q.GetProperty("snapshotHash").GetString() });
        converted.EnsureSuccessStatusCode();
        return await AcceptAsync(package, (await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
    }

    private static async Task WaitForFenceAsync(P0213Fixture delivery, int count)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            var n = await delivery.MerchantScalarAsync("""
                SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory'
                    AND query LIKE '%take_merchant_licence_fence%' AND cardinality(pg_blocking_pids(pid))>0
                """);
            if ((long)n! >= count) return;
            await Task.Delay(10, limit.Token);
        }
    }
}
