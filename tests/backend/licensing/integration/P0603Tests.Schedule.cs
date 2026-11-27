using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0603Tests
{
    [Fact]
    public async Task ResolvedSelectionSchedulesOnceAndReplaysOnlyForTheLiveOwner()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var originalTermRow = await P0308Tests.TermRowAsync(fixture, term);

        // Give Shop[1] 4 active members (exceeds solo allowance 2).
        for (var i = 0; i < 3; i++)
            await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, package.Shops[1].ShopId);

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Plan", 1, 2, 6000L);
        fixture.At(February14);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);

        // Selecting an over-limit shop is refused without modifying the roster.
        using var conflict = await PostScheduleAsync(package, term, preview, [package.Shops[1].ShopId]);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(CapacityReductionsV1.MemberConflict, await P0206Fixture.CodeAsync(conflict));
        Assert.Equal(4L, await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE shop_id = '{package.Shops[1].ShopId:D}' AND status = 'Active'"));

        // Fabricated or wrong-count shop selection is refused.
        using var fabricated = await PostScheduleAsync(package, term, preview, [Guid.NewGuid()]);
        Assert.Equal(HttpStatusCode.BadRequest, fabricated.StatusCode);
        using var tooMany = await PostScheduleAsync(package, term, preview, [package.Shops[0].ShopId, package.Shops[1].ShopId]);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        // Manager and unrelated owner cannot preview, schedule, read, or recover.
        var manager = package.Shops[1];
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES (gen_random_uuid(), '{package.Account:D}', '{manager.UserId:D}', 2, 1, 1, now())
            """);
        var foreign = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        foreach (var caller in new[] { manager, foreign })
        {
            using var denyPreview = await PostPreviewAsync(package, term, solo, 1, 3, 5, February29, caller: caller.Client);
            Assert.Equal(HttpStatusCode.Forbidden, denyPreview.StatusCode);
            using var denySchedule = await PostScheduleAsync(package, term, preview, [package.Shops[0].ShopId], caller: caller.Client);
            Assert.Equal(HttpStatusCode.Forbidden, denySchedule.StatusCode);
        }

        // Expired preview is refused once clock advances past 15 minutes.
        fixture.At(February14.AddMinutes(15));
        using var expired = await PostScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal(CapacityReductionsV1.Expired, await P0206Fixture.CodeAsync(expired));

        // Fresh preview + concurrent two-tab schedule race: exactly one schedule committed.
        fixture.At(February14.AddMinutes(16));
        var fresh = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var key1 = Guid.NewGuid();
        var key2 = Guid.NewGuid();
        var responses = await Task.WhenAll(
            PostScheduleAsync(package, term, fresh, [package.Shops[0].ShopId], key1),
            PostScheduleAsync(package, term, fresh, [package.Shops[0].ShopId], key2));
        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
        }
        Assert.Equal(1L, await fixture.CountAsync("scheduled_capacity_reductions", $"term_id = '{term:D}'"));
        var saved = await ReadScheduleAsync(package, term);
        Assert.Equal("Scheduled", saved.State);
        Assert.Equal(1, saved.RetainedShopCount);
        Assert.Equal(2, saved.SourceShopCount);
        Assert.Equal(6000L, saved.AmountPaise);
        Assert.Equal(February29, saved.EffectiveAt);

        // Find whichever key won; verify idempotent 200 replay, 409 mismatch on changed payload, and recovery route.
        var winnerKey = (await fixture.ScalarAsync(
            $"SELECT operation_key FROM scheduled_capacity_reductions WHERE schedule_id = '{saved.ScheduleId:D}'")) is Guid k ? k : Guid.Empty;
        using var replay = await PostScheduleAsync(package, term, fresh, [package.Shops[0].ShopId], winnerKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(saved.ScheduleId, (await replay.Content.ReadFromJsonAsync<ScheduledCapacityReductionV1>())!.ScheduleId);

        using var mismatch = await PostScheduleAsync(package, term, fresh, [package.Shops[1].ShopId], winnerKey);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(mismatch));

        using var recovered = await package.Payer.Client.GetAsync($"{ReductionsPath(package.Account, term)}/operations/{winnerKey:D}");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(saved.ScheduleId, (await recovered.Content.ReadFromJsonAsync<ScheduledCapacityReductionV1>())!.ScheduleId);

        // Paged frozen schedule shops explicitly mark retained vs omitted.
        using var shopsRes = await package.Payer.Client.GetAsync($"{ReductionsPath(package.Account, term)}/{saved.ScheduleId:D}/shops");
        shopsRes.EnsureSuccessStatusCode();
        var frozenPage = await shopsRes.Content.ReadFromJsonAsync<CapacityReductionShopPageV1>();
        Assert.True(Assert.Single(frozenPage!.Items, s => s.ShopId == package.Shops[0].ShopId).Retained);
        Assert.False(Assert.Single(frozenPage.Items, s => s.ShopId == package.Shops[1].ShopId).Retained);

        // Current paid term and rosters remain completely untouched.
        Assert.Equal(originalTermRow, await P0308Tests.TermRowAsync(fixture, term));
        var activeStatus = await AccountTermAsync(package, term);
        Assert.Equal(3, activeStatus.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(5, activeStatus.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(February29, activeStatus.GetProperty("paidThrough").GetDateTimeOffset());

        // Demoting the former owner (while promoting another owner) denies replay and recovery BEFORE lookup/replay.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE billing_account_members
            SET role = CASE WHEN merchant_user_id = '{package.Payer.UserId:D}' THEN 2 ELSE 1 END
            WHERE account_id = '{package.Account:D}' AND merchant_user_id IN ('{package.Payer.UserId:D}', '{manager.UserId:D}')
            """);
        using var demotedReplay = await PostScheduleAsync(package, term, fresh, [package.Shops[0].ShopId], winnerKey);
        Assert.Equal(HttpStatusCode.Forbidden, demotedReplay.StatusCode);
        using var demotedRecover = await package.Payer.Client.GetAsync($"{ReductionsPath(package.Account, term)}/operations/{winnerKey:D}");
        Assert.Equal(HttpStatusCode.Forbidden, demotedRecover.StatusCode);
    }
}
