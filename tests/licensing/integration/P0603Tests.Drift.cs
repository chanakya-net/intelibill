using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0603Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdmissionAndReductionConsumeSerializeAtTheFenceAndRecoverWithOriginalFunds(bool consumeFirst)
    {
        // Seed a 1-month paid predecessor whose renewal boundary lands 45 seconds ahead of real wall time so
        // affiliation setup finishes before paidThrough while JwtBearer (5m skew) and all clocks stay aligned.
        var now = P0202Fixture.WholeSecondNow();
        var istEnd = now.AddSeconds(45).ToOffset(TimeSpan.FromHours(5.5));
        var istStart = istEnd.AddMonths(-1);
        var anchor = new BillingAnchor(DateTime.SpecifyKind(istStart.DateTime, DateTimeKind.Unspecified),
            BillingCalendar.Zone, BillingPeriod.Month);
        var startsAt = BillingCalendar.TryBoundary(anchor, 0)!.Value;
        var paidThrough = BillingCalendar.TryBoundary(anchor, 1)!.Value;
        var scheduleAt = paidThrough.AddSeconds(-30);

        await using var fixture = await StartAsync(database, scheduleAt);
        var payer = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var shop1 = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var shop2 = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(payer.Client);
        var term = await P0209Fixture.SeedTermAsync(fixture.Hosts, account, new P0209Fixture.TermSeed(
            startsAt, paidThrough, Grouped: true, Capacity: 3, MemberAllowance: 5, PricePaise: 12000L,
            AnchorLocal: anchor.LocalStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        var shops = new[] { payer, shop1, shop2 };
        foreach (var shop in shops)
            await P0213Fixture.LinkAsync(account, payer, shop, term);
        var package = new Package(payer, account, shops, Guid.Empty);

        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        // Retain Shops[0] (routing) and Shops[1] (non-routing!). Admit 1 active + 1 suspended staff on Shops[1]
        // so Shops[1] starts at 2 active members (fitting target allowance 2) with 1 ready to activate in the race.
        var nonRouting = package.Shops[1];
        await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, nonRouting.ShopId);
        var candidate = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, nonRouting.ShopId);
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb,
            $"UPDATE shop_memberships SET status = 'Suspended', authority_version = 1 WHERE shop_id = '{nonRouting.ShopId:D}' AND user_id = '{candidate.UserId:D}'");
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb,
            $"UPDATE shop_license_states SET enforcement_activated_at = now() WHERE shop_id = '{nonRouting.ShopId:D}'");

        var targetPlan = await PublishReductionPlanAsync(fixture, "multi-shop", "Tight Dual", 2, 2, 8500L);
        var preview = await CreatePreviewAsync(package, term, targetPlan, 2, 3, 5, paidThrough);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId, nonRouting.ShopId]);
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        await PayAsync(package, order);

        // Reach the exact renewal boundary at real wall time (<= +2s from DateTime.UtcNow): all clocks align!
        var boundaryAt = paidThrough > now ? paidThrough : P0202Fixture.WholeSecondNow();
        fixture.At(boundaryAt);
        fixture.Hosts.Fixture.Clock.Now = boundaryAt;
        delivery.Transport.LeaseClock.Now = boundaryAt;

        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);  // delivers fresh snapshots + Manifest + Reserve
        await delivery.DrainAsync(false); // delivers Reserved receipt -> licensing queues ReductionConsume
        Assert.Equal(1L, await fixture.CountAsync("entitlement_messages", "kind = 'ReductionConsume'"));

        // Hold the exact merchant account advisory fence so Consume and non-routing admission queue behind it in chosen order.
        await using var gate = new NpgsqlConnection(delivery.MerchantDb.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('a{package.Account:D}', 859))", gate, tx);
        await hold.ExecuteNonQueryAsync();

        Task consume;
        Task<HttpResponseMessage> admit;
        if (consumeFirst)
        {
            consume = delivery.DrainAsync(true);
            await WaitForReductionFenceAsync(delivery, 1);
            admit = UpdateMemberStatusAsync(nonRouting, candidate.UserId, "Active", expectedVersion: 1);
        }
        else
        {
            admit = UpdateMemberStatusAsync(nonRouting, candidate.UserId, "Active", expectedVersion: 1);
            await WaitForReductionFenceAsync(delivery, 1);
            consume = delivery.DrainAsync(true);
        }
        await WaitForReductionFenceAsync(delivery, 2);
        await tx.CommitAsync();

        using var admitted = await admit;
        await consume;
        await delivery.DrainAsync(false);

        if (consumeFirst)
        {
            // Consume won: immediate reduced allowance 2 enforced on non-routing shop BEFORE successor snapshot even arrives!
            Assert.Equal(HttpStatusCode.Conflict, admitted.StatusCode);
            Assert.Equal("Licence.MemberCapacityReached", await P0206Fixture.CodeAsync(admitted));
            var applied = await ReadScheduleAsync(package, term);
            Assert.Equal("Applied", applied.State);
            Assert.Equal(2L, await delivery.MerchantScalarAsync(
                $"SELECT count(*) FROM shop_memberships WHERE shop_id = '{nonRouting.ShopId:D}' AND status = 'Active'"));
            return;
        }

        // Admission won: drift blocks application into NeedsResolution; nobody is silently removed or demoted!
        Assert.True(admitted.IsSuccessStatusCode, await admitted.Content.ReadAsStringAsync());
        var blocked = await ReadScheduleAsync(package, term);
        Assert.Equal("NeedsResolution", blocked.State);
        Assert.Equal("MemberCapacityExceeded", blocked.Reason);
        Assert.NotNull(blocked.Conflicts);
        var conflict = Assert.Single(blocked.Conflicts!);
        Assert.Equal(nonRouting.ShopId, conflict.ShopId);
        Assert.Equal(3, conflict.ActiveMembers);
        Assert.Equal(2, conflict.TargetAllowance);
        Assert.Equal(3L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE shop_id = '{nonRouting.ShopId:D}' AND status = 'Active'"));

        // Resolving before reducing staff fails with MemberConflict.
        using var premature = await PostResolveAsync(package, term, blocked);
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        Assert.Equal(CapacityReductionsV1.MemberConflict, await P0206Fixture.CodeAsync(premature));

        // Owner suspends the extra staff member and resolves -> original funded order applies at generation 2 with NO second charge!
        using var suspended = await UpdateMemberStatusAsync(nonRouting, candidate.UserId, "Suspended", expectedVersion: 2);
        Assert.True(suspended.IsSuccessStatusCode, await suspended.Content.ReadAsStringAsync());
        var resolveKey = Guid.NewGuid();
        using var resolved = await PostResolveAsync(package, term, blocked, resolveKey);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        // Idempotent replay of resolution returns the same schedule.
        using var resolveReplay = await PostResolveAsync(package, term, blocked, resolveKey);
        Assert.Equal(HttpStatusCode.OK, resolveReplay.StatusCode);

        await DrainReductionAsync(delivery, rounds: 4);
        var final = await ReadScheduleAsync(package, term);
        Assert.Equal("Applied", final.State);
        Assert.Equal(2L, final.ResolutionGeneration);
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{order.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id = '{order.OrderId:D}'"));
    }

    [Fact]
    public async Task ExpiredReserveRecoversWithResolutionAndDuplicateReceiptIsIdempotent()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Recover", 1, 3, 6000L);
        fixture.At(February14);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        await PayAsync(package, order);

        // Queue Manifest + Reserve at paidThrough, then advance clock past 5-minute reserve expiry before Consume executes.
        fixture.At(February29);
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.Equal(1L, await fixture.CountAsync("entitlement_messages", "kind = 'ReductionConsume'"));

        fixture.At(February29.AddMinutes(6));
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var expired = await ReadScheduleAsync(package, term);
        Assert.Equal("NeedsResolution", expired.State);
        Assert.Equal("ReservationExpired", expired.Reason);

        // Duplicate delivery of the signed merchant receipt is idempotent (204 NoContent).
        var consumeEvent = await fixture.ScalarAsync(
            $"SELECT consume_event FROM scheduled_capacity_reductions WHERE schedule_id = '{schedule.ScheduleId:D}'");
        var receiptBytes = (byte[])(await delivery.MerchantScalarAsync(
            $"SELECT payload FROM entitlement_messages WHERE event_id = '{consumeEvent}'"))!;
        using var signed = await delivery.Transport.SignedAsync(receiptBytes, delivery.AuthClock,
            merchant: true, path: ReductionTransportV1.ReceiptPath);
        using var duplicate = await delivery.LicensingApi.SendAsync(signed);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);

        // Owner resolves with original funds -> generation 2 applies cleanly!
        using var resolved = await PostResolveAsync(package, term, expired);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        await DrainReductionAsync(delivery, rounds: 4);
        var applied = await ReadScheduleAsync(package, term);
        Assert.Equal("Applied", applied.State);
        Assert.Equal(2L, applied.ResolutionGeneration);
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{order.OrderId:D}'"));
    }

    [Fact]
    public async Task AffiliationBindingDriftWithoutChangingShopCountEntersFinanceReview()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo AffDrift", 1, 3, 6000L);
        fixture.At(February14);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        await PayAsync(package, order);

        // Simulate an affiliation binding drift on the merchant side while keeping the exact same 2 shops.
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb, $"""
            UPDATE public.shop_license_states
            SET affiliation_request_id = gen_random_uuid(), revision = revision + 1
            WHERE shop_id = '{package.Shops[1].ShopId:D}'
            """);

        fixture.At(February29);
        await DrainReductionAsync(delivery, rounds: 4);

        var reviewed = await ReadScheduleAsync(package, term);
        Assert.Equal("NeedsReview", reviewed.State);
        Assert.Equal("SourceChanged", reviewed.Reason);
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(1L, await fixture.CountAsync("unmatched_payments",
            $"order_id = '{order.OrderId:D}' AND classification = 'SourceChanged'"));
    }

    private static async Task WaitForReductionFenceAsync(P0213Fixture delivery, int count)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            var n = await delivery.MerchantScalarAsync("""
                SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory'
                    AND query LIKE '%take_merchant_licence_fence%' AND cardinality(pg_blocking_pids(pid)) > 0
                """);
            if ((long)n! >= count) return;
            await Task.Delay(10, limit.Token);
        }
    }
}
