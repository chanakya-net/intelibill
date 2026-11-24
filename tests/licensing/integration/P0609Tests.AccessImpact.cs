using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task RefundPreviewDistinguishesCoverageFromRestrictionClosureAndCourtesy()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, named, _, request) = await TwoSubscriptionsAsync(fixture);
        var delivery = fixture.Checkout.Delivery;
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        using var security = await P0417Tests.SignInAsync(delivery, "security-administrator");
        using var manager = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(
            fixture.Checkout.Trial.CatalogueManagerSeed!, P0114Tests.Invitee);
        using var billing = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);
        var scheduled = Instant.AddMinutes(1);
        await P0403Tests.ApplySuspensionAsync(security, package.Account, "Shop", named.Shops[0].ShopId, "Trading", scheduled);
        await P0212Fixture.GrantAsync(manager, package.Account, P0212Fixture.Draft([named.Shops[1].ShopId],
            Instant, Boundary.AddDays(1)));
        var before = await PreviewAsync(billing, package.Account, request, 4500, "EndAt", Boundary);
        var shops = before.GetProperty("access").GetProperty("shops").EnumerateArray().ToDictionary(s => s.GetProperty("shopId").GetGuid());
        Assert.False(shops[named.Shops[0].ShopId].GetProperty("restricted").GetBoolean());
        Assert.Equal(scheduled, shops[named.Shops[0].ShopId].GetProperty("scheduledRestrictionAt").GetDateTimeOffset());
        Assert.Equal(Boundary.AddDays(1), shops[named.Shops[1].ShopId].GetProperty("courtesyEndsAt").GetDateTimeOffset());
        Assert.Equal("Active", (await TermAsync(named.Shops[0])).GetProperty("state").GetString());
        await P0417Tests.CloseShopAsync(named.Shops[0], package.Account);
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, request, Command(before, 4500, "EndAt", Boundary)),
            HttpStatusCode.Conflict, "Refund.PreviewStale");
        fixture.At(scheduled);
        var latest = await PreviewAsync(billing, package.Account, request, 4500, "EndAt", Boundary);
        var closed = latest.GetProperty("access").GetProperty("shops").EnumerateArray()
            .Single(s => s.GetProperty("shopId").GetGuid() == named.Shops[0].ShopId);
        Assert.True(closed.GetProperty("closed").GetBoolean());
        Assert.True(closed.GetProperty("restricted").GetBoolean());
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(latest, 4500, "EndAt", Boundary)),
            HttpStatusCode.Created);
        fixture.At(Boundary);
        Assert.Equal("Closed", (await delivery.LatestSnapshotAsync(named.Shops[0].ShopId)).Shop!.State);
        Assert.Equal("Complimentary", (await TermAsync(named.Shops[1])).GetProperty("basis").GetString());
    }

    [Fact]
    public async Task AdjustedImportedTermRejectsOldCancellationAndPendingShopConsent()
    {
        var now = P0202Fixture.WholeSecondNow().AddSeconds(1);
        await using var fixture = await StartAsync(database, now, P0306Tests.Settings);
        fixture.Hosts.Fixture.Clock.Now = now;
        var owner = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var extra = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(owner.Client);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial);
        var start = now.AddDays(-1);
        var end = start.AddMonths(1);
        var term = await P0209Fixture.SeedTermAsync(fixture.Hosts, account, new(start, end,
            AnchorLocal: start.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            PricePaise: 12000, Grouped: true, Capacity: 3, PlanVersionId: plan));
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        var package = new Package(owner, account, [owner], plan);
        var cancelPath = P0215Tests.CancellationPath(account, term);
        var cancellation = await P0215Fixture.PreviewAsync(owner, cancelPath);
        var consent = await P0215Fixture.RequestAsync(owner, extra, account, term);
        Assert.Equal("Granted", await P0205Tests.DecideAsync(extra.Client, consent, "grant"));
        var order = await P0602Tests.IncreaseSeatsAsync(fixture, package, term, end);
        await fixture.Checkout.Delivery.ScanAsync(true);
        await P0608Tests.DrainAsync(fixture);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ReadAsync(await P0212Fixture.PostAsync(billing, P0306Tests.Path(order), P0306Tests.Receipt(order, now)),
            HttpStatusCode.Created);
        await P0608Tests.DrainAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", "state='Applied'"));
        var confirmation = await P0608Tests.ConfirmationIdAsync(owner.Client, account, order.OrderId);
        var request = await RequestRefundAsync(package, order.OrderId, confirmation, order.AmountPaise);
        var boundary = now.AddMinutes(1);
        var preview = await PreviewAsync(billing, account, request, 100, "EndAt", boundary);
        await ReadAsync(await ApproveAsync(billing, account, request, Command(preview, 100, "EndAt", boundary)), HttpStatusCode.Created);
        fixture.At(boundary);
        fixture.Hosts.Fixture.Clock.Now = boundary;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.GetAsync(cancelPath)).StatusCode);
        using var stale = await owner.Client.PostAsJsonAsync(cancelPath, P0215Tests.Accept(cancellation, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var list = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{account}/affiliations");
        Assert.Empty(list.GetProperty("terms").EnumerateArray());
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(extra.Client, consent, "accept"));
        await P0205Tests.WaitForConsentAsync(extra.Client, consent, "NeedsReview");
        Assert.Equal(0L, await fixture.CountAsync("shop_affiliations", $"shop_id='{extra.ShopId}'"));
    }

    [Theory]
    [InlineData("EndAt", 0)]
    [InlineData("TermAdjustment", 7)]
    public async Task AdjustedImportedTermAllowsSpareSlotAssignmentAndRestoredPurchase(string treatment, int graceDays)
    {
        var now = P0202Fixture.WholeSecondNow().AddSeconds(1);
        var deliveries = new AffiliationDeliveryRecorder();
        await using var fixture = await StartAsync(database, now, P0306Tests.Settings,
            services => services.AddSingleton<IStartupFilter>(deliveries));
        fixture.Hosts.Fixture.Clock.Now = now;
        await VerifyAffiliationMigrationRoundTripAsync(fixture);

        var boundary = now.AddDays(5);
        var (package, term, originalEnd) = await SeedAdjustedImportedTermAsync(fixture, now, treatment, boundary);
        var extra = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        await AssignSpareSlotBeforeBoundaryAsync(fixture, package, extra, term, boundary, deliveries);
        await AssertForgedCoverageEndRefusedAsync(fixture, package.Account, package.Payer.UserId, term, now, boundary, originalEnd);

        foreach (var shop in new[] { package.Payer, extra })
        {
            var active = await TermAsync(shop);
            Assert.Equal("Active", active.GetProperty("state").GetString());
            Assert.Equal(boundary, active.GetProperty("paidThrough").GetDateTimeOffset());
            Assert.Equal(boundary.AddDays(graceDays), active.GetProperty("accessEndsAt").GetDateTimeOffset());
        }

        if (graceDays > 0)
        {
            fixture.At(boundary);
            foreach (var shop in new[] { package.Payer, extra })
                Assert.Equal("Grace", (await TermAsync(shop)).GetProperty("state").GetString());
            using var premature = await TryRenewalQuoteAsync(package, term, "Month");
            Assert.Equal(HttpStatusCode.BadRequest, premature.StatusCode);
        }

        await AssertRestoredPurchaseCoversBothShopsAsync(fixture, package, extra, term, boundary.AddDays(graceDays).AddSeconds(1));
        await RecordAsync("spare-slot-" + treatment, new
        {
            treatment, boundary, graceDays, spareShopAssignedBeforeBoundary = extra.ShopId,
            deliveryStatuses = deliveries.Statuses, forgedDirectInsertsRefused = 3, restoredShopCount = 2,
        });
    }

    private static async Task AssertRestoredPurchaseCoversBothShopsAsync(P0308Fixture fixture, Package package,
        P0209Fixture.Merchant extra, Guid term, DateTimeOffset restoredAt)
    {
        fixture.At(restoredAt);
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, term, "Month"));
        Assert.Equal("RestoredAccess", quote.StartRule);
        Assert.Equal(restoredAt, quote.PreviewStartsAt);
        Assert.Equal(2, quote.ShopCount);
        Assert.Equal(4, quote.Capacity);
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);
        for (var delivery = 0; delivery < 2; delivery++)
            P0302Fixture.AssertStatus(await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt),
                "Verified", "Fulfilled", "Applying");
        await P0608Tests.DrainAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{term:D}'"));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"order_id = '{quote.OrderId:D}'"));
        foreach (var shop in new[] { package.Payer, extra })
        {
            var restored = await TermAsync(shop);
            Assert.Equal("Active", restored.GetProperty("state").GetString());
            Assert.Equal(restoredAt, restored.GetProperty("startsAt").GetDateTimeOffset());
        }
    }

    private static async Task VerifyAffiliationMigrationRoundTripAsync(P0308Fixture fixture)
    {
        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var downgraded = (string)(await fixture.ScalarAsync(
                "SELECT pg_get_functiondef('public.guard_shop_affiliation_coverage()'::regprocedure)"))!;
            Assert.DoesNotContain("refund_boundary", downgraded);
            Assert.Contains("NEW.coverage_ends_at = t.ends_at", downgraded);
            await migrator.MigrateAsync();
            var upgraded = (string)(await fixture.ScalarAsync(
                "SELECT pg_get_functiondef('public.guard_shop_affiliation_coverage()'::regprocedure)"))!;
            Assert.Contains("reviewed_refund_paid_through", upgraded);
            Assert.Contains("refund_boundary", upgraded);
        }
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        SetCredentialTime(fixture, fixture.Checkout.Trial.Catalogue.Clock.GetUtcNow());
    }

    private static async Task<(Package Package, Guid Term, DateTimeOffset OriginalEnd)> SeedAdjustedImportedTermAsync(
        P0308Fixture fixture, DateTimeOffset now, string treatment, DateTimeOffset boundary)
    {
        var owner = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(owner.Client);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial);
        var start = now.AddDays(-1);
        var end = start.AddMonths(1);
        var term = await P0209Fixture.SeedTermAsync(fixture.Hosts, account, new(start, end,
            AnchorLocal: start.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            PricePaise: 12000, Grouped: true, Capacity: 3, PlanVersionId: plan));
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        var package = new Package(owner, account, [owner], plan);
        var order = await P0602Tests.IncreaseSeatsAsync(fixture, package, term, end);
        await fixture.Checkout.Delivery.ScanAsync(true);
        await P0608Tests.DrainAsync(fixture);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ReadAsync(await P0212Fixture.PostAsync(billing, P0306Tests.Path(order), P0306Tests.Receipt(order, now)),
            HttpStatusCode.Created);
        await P0608Tests.DrainAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(owner.Client, account, order.OrderId);
        var request = await RequestRefundAsync(package, order.OrderId, confirmation, order.AmountPaise);
        var preview = await PreviewAsync(billing, account, request, 100, treatment, boundary);
        await ReadAsync(await ApproveAsync(billing, account, request, Command(preview, 100, treatment, boundary)), HttpStatusCode.Created);
        await P0608Tests.DrainAsync(fixture);
        return (package with { Plan = order.PlanVersionId }, term, end);
    }

    private static async Task AssignSpareSlotBeforeBoundaryAsync(P0308Fixture fixture, Package package,
        P0209Fixture.Merchant extra, Guid term, DateTimeOffset boundary, AffiliationDeliveryRecorder deliveries)
    {
        var list = await package.Payer.Client.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{package.Account:D}/affiliations");
        var offered = Assert.Single(list.GetProperty("terms").EnumerateArray());
        Assert.Equal(term, offered.GetProperty("termId").GetGuid());
        Assert.Equal(boundary, offered.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(4, offered.GetProperty("capacity").GetInt32());

        var key = Guid.NewGuid();
        var body = new { operationKey = key, shopId = extra.ShopId, termId = term, withoutCoverage = false };
        var created = await ReadAsync(await package.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{package.Account:D}/affiliations", body), HttpStatusCode.Created);
        var replayed = await ReadAsync(await package.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{package.Account:D}/affiliations", body), HttpStatusCode.Created);
        var requestId = created.GetProperty("requestId").GetGuid();
        Assert.Equal(requestId, replayed.GetProperty("requestId").GetGuid());
        Assert.Equal(boundary, created.GetProperty("term").GetProperty("endsAt").GetDateTimeOffset());

        deliveries.Reset();
        var consent = $"/api/billing/accounts/{package.Account:D}/affiliations/{extra.ShopId:D}/{requestId:D}/consent";
        Assert.Equal("Granted", await P0205Tests.DecideAsync(extra.Client, consent, "grant"));
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(extra.Client, consent, "accept"));
        var status = await deliveries.WaitNextAsync();
        Assert.Equal(200, status);
        Assert.DoesNotContain(503, deliveries.Statuses);
        await P0205Tests.WaitForConsentAsync(extra.Client, consent, "Applied");
        await P0608Tests.DrainAsync(fixture);
        var savedEnd = (DateTime)(await fixture.ScalarAsync(
            $"SELECT coverage_ends_at FROM public.shop_affiliations WHERE shop_id = '{extra.ShopId:D}'"))!;
        Assert.Equal(boundary, new DateTimeOffset(DateTime.SpecifyKind(savedEnd, DateTimeKind.Utc)));
    }

    private static async Task AssertForgedCoverageEndRefusedAsync(P0308Fixture fixture, Guid account, Guid actor,
        Guid term, DateTimeOffset now, DateTimeOffset boundary, DateTimeOffset originalEnd)
    {
        await using var connection = new NpgsqlConnection(fixture.Hosts.Database.Login(
            fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime").ConnectionString);
        await connection.OpenAsync();
        foreach (var (appliedAt, endsAt, reviewed) in new (DateTimeOffset, DateTimeOffset, DateTimeOffset?)[]
            { (now, originalEnd, boundary), (boundary, boundary, boundary), (now, boundary, null) })
        {
            await using var tx = await connection.BeginTransactionAsync();
            var (request, shop) = (Guid.NewGuid(), Guid.NewGuid());
            await using (var setup = new NpgsqlCommand("""
                WITH scope AS (SELECT set_config('app.billing_account_id', $1, true), set_config('app.billing_actor_id', $2, true))
                INSERT INTO public.shop_affiliation_requests (request_id, account_id, account_name, shop_id, requester_id,
                    requester_revision, operation_key, term_id, created_at, expires_at, reviewed_refund_paid_through)
                SELECT $3, $4, 'Renewal Fixture', $5, $6, 1, gen_random_uuid(), $7, $8, $8 + interval '7 days', $9 FROM scope
                """, connection, tx))
            {
                foreach (var value in new object[] { account.ToString("D"), actor.ToString("D"), request, account, shop, actor, term, now })
                    setup.Parameters.AddWithValue(value);
                setup.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)reviewed ?? DBNull.Value);
                await setup.ExecuteNonQueryAsync();
            }
            await using var forged = new NpgsqlCommand("""
                INSERT INTO public.shop_affiliations (request_id, account_id, shop_id, term_id, event_id, actor_id,
                    membership_id, authority_version, shop_name, owner_name, accepted_at, applied_at, coverage_ends_at)
                VALUES ($1, $2, $3, $4, gen_random_uuid(), $5, gen_random_uuid(), 1, 'Forged', 'Owner', $6, $7, $8)
                """, connection, tx);
            foreach (var value in new object[] { request, account, shop, term, actor, now, appliedAt, endsAt })
                forged.Parameters.AddWithValue(value);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => forged.ExecuteNonQueryAsync())).SqlState);
        }
    }

    private sealed class AffiliationDeliveryRecorder : IStartupFilter
    {
        private ConcurrentQueue<int> _statuses = new();
        private TaskCompletionSource<int> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int[] Statuses => [.. _statuses];
        internal void Reset() { _statuses = new(); _first = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal Task<int> WaitNextAsync() => _first.Task.WaitAsync(TimeSpan.FromSeconds(15));

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, pipeline) =>
            {
                await pipeline(context);
                if (context.Request.Path != ShopAffiliationsV1.DeliveryPath) return;
                _statuses.Enqueue(context.Response.StatusCode);
                _first.TrySetResult(context.Response.StatusCode);
            });
            next(app);
        };
    }
}
