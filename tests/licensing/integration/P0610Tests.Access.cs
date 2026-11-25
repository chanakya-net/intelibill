using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task RetainedApprovalKeepsEffectiveAccessWithoutDispatch()
    {
        var now = P0202Fixture.WholeSecondNow().AddSeconds(1);
        await using var fixture = await P0609Tests.StartAsync(database, now);
        fixture.Hosts.Fixture.Clock.Now = now;
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        await P0608Tests.DrainAsync(fixture);
        var boundary = now.AddMinutes(2);
        var snapshotsBeforeApproval = await fixture.CountAsync("entitlement_snapshots");
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);

        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261125100918_RefundApprovals");

        // Exercise P06-09 approval + immediate per-shop snapshot publication against the pre-P06-10 schema.
        await ApproveEndAsync(finance, package.Account, request, boundary);
        await PublishLegacyP0609ApprovalSnapshotsAsync(fixture, package, boundary);
        await P0608Tests.DrainAsync(fixture);

        var delivery = fixture.Checkout.Delivery;
        Assert.Equal(snapshotsBeforeApproval + package.Shops.Length, await fixture.CountAsync("entitlement_snapshots"));
        var retainedBefore = await ReadEntitlementLedgerAsync(fixture.Hosts.LicensingOwner);
        var acksBefore = await ReadShopAcknowledgementsAsync(fixture.Hosts.LicensingOwner);
        var merchantBefore = new Dictionary<Guid, (long Revision, DateTime AccessEndsAt)>();
        foreach (var shop in package.Shops)
        {
            var expectedRev = acksBefore[shop.ShopId];
            var state = await ReadMerchantLicenseStateAsync(delivery.MerchantDb, shop.ShopId);
            Assert.Equal(expectedRev, state.Revision);
            Assert.Equal(boundary.UtcDateTime, state.AccessEndsAt);
            merchantBefore[shop.ShopId] = state;
            await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb, $"""
                UPDATE public.shop_license_states SET enforcement_activated_at = '{now:O}'::timestamptz
                WHERE shop_id = '{shop.ShopId:D}'
                """);
            using var authorityBefore = await shop.Client.GetAsync(P0418Tests.AuthorityPath(shop.ShopId));
            var enforcedBefore = await ReadAsync(authorityBefore, HttpStatusCode.OK);
            Assert.True(enforcedBefore.GetProperty("enforced").GetBoolean());
            Assert.Equal(boundary, enforcedBefore.GetProperty("validUntil").GetDateTimeOffset());
        }

        // Upgrade through P06-10+ migrations, restore runtime grants, and run reconciliation and delivery twice.
        await migrator.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);

        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_attempts"));
        Assert.Equal(0L, await fixture.CountAsync("refund_work"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(snapshotsBeforeApproval + package.Shops.Length, await fixture.CountAsync("entitlement_snapshots"));
        Assert.Equal(retainedBefore, await ReadEntitlementLedgerAsync(fixture.Hosts.LicensingOwner));
        Assert.Equal(acksBefore, await ReadShopAcknowledgementsAsync(fixture.Hosts.LicensingOwner));

        var item = await MerchantOutcomeAsync(package);
        Assert.Equal("Historical", item.GetProperty("providerState").GetString());
        Assert.Equal("Applied", item.GetProperty("accessDisposition").GetString());
        Assert.Equal(4500L, item.GetProperty("approvedAmountPaise").GetInt64());
        Assert.Equal(6000L, item.GetProperty("amountPaise").GetInt64());
        foreach (var shop in package.Shops)
            await AssertRetainedMerchantBoundaryAsync(fixture, package, shop, merchantBefore[shop.ShopId], boundary);
    }

    private static async Task AssertRetainedMerchantBoundaryAsync(P0308Fixture fixture, P0308Fixture.Package package,
        P0209Fixture.Merchant shop, (long Revision, DateTime AccessEndsAt) expectedState, DateTimeOffset boundary)
    {
        Assert.Equal(expectedState, await ReadMerchantLicenseStateAsync(fixture.Checkout.Delivery.MerchantDb, shop.ShopId));
        fixture.At(boundary.AddTicks(-10));
        fixture.Hosts.Fixture.Clock.Now = boundary.AddTicks(-10);
        var beforeEnd = await P0308Fixture.ShopTermAsync(package, shop);
        Assert.Equal("Active", beforeEnd.GetProperty("state").GetString());
        Assert.Equal(boundary, beforeEnd.GetProperty("accessEndsAt").GetDateTimeOffset());
        using var allowed = await shop.Client.GetAsync(P0418Tests.AuthorityPath(shop.ShopId));
        var allowedBody = await ReadAsync(allowed, HttpStatusCode.OK);
        Assert.True(allowedBody.GetProperty("enforced").GetBoolean());
        Assert.Equal(boundary, allowedBody.GetProperty("validUntil").GetDateTimeOffset());
        fixture.At(boundary);
        fixture.Hosts.Fixture.Clock.Now = boundary;
        Assert.Equal("Expired", (await P0308Fixture.ShopTermAsync(package, shop)).GetProperty("state").GetString());
        using var blocked = await shop.Client.GetAsync(P0418Tests.AuthorityPath(shop.ShopId));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("Licence.Expired", await P0206Fixture.CodeAsync(blocked));
    }

    private static async Task PublishLegacyP0609ApprovalSnapshotsAsync(P0308Fixture fixture, P0308Fixture.Package package,
        DateTimeOffset boundary)
    {
        var deliveryNow = EntitlementSnapshotsV1.Instant(fixture.Hosts.Fixture.Clock);
        var decidedAt = new DateTimeOffset(DateTime.SpecifyKind(
            (DateTime)(await fixture.ScalarAsync("SELECT decided_at FROM public.refund_access_decisions"))!, DateTimeKind.Utc));
        await using var conn = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var shop in package.Shops)
        {
            var prior = await fixture.Checkout.Delivery.LatestSnapshotAsync(shop.ShopId);
            await using var revCmd = new NpgsqlCommand("""
                UPDATE public.entitlement_shop_heads SET revision = revision + 1, next_verification_at = $1
                WHERE shop_id = $2 AND account_id = $3 RETURNING revision
                """, conn, tx);
            revCmd.Parameters.AddWithValue(boundary);
            revCmd.Parameters.AddWithValue(shop.ShopId);
            revCmd.Parameters.AddWithValue(package.Account);
            var nextRev = (long)(await revCmd.ExecuteScalarAsync())!;
            var eventId = Guid.NewGuid();
            var shopHead = new ShopEntitlementSnapshotV1(nextRev, decidedAt, "Active", prior.Shop!.TermId,
                prior.Shop.Basis, prior.Shop.StartsAt, boundary, boundary, prior.Shop.ShopCapacity, prior.Shop.MemberAllowance);
            var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, eventId, package.Account,
                shop.ShopId, prior.AffiliationRequestId, prior.Account, shopHead);
            var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
            var digest = SignedServiceAssertion.Digest(payload);
            await using var insert = new NpgsqlCommand("""
                WITH msg AS (
                    INSERT INTO public.entitlement_messages (event_id, account_id, shop_id, kind, payload, digest)
                    VALUES ($1, $2, $3, 'Snapshot', $4, $5) RETURNING event_id
                ), box AS (
                    INSERT INTO public.entitlement_outbox (event_id, account_id, shop_id, next_attempt_at)
                    SELECT event_id, $2, $3, $6 FROM msg
                )
                INSERT INTO public.entitlement_snapshots
                    (event_id, account_id, shop_id, affiliation_request_id, account_revision, shop_revision, closure_revision, publisher_protocol)
                SELECT event_id, $2, $3, $7, $8, $9, NULL, 2 FROM msg
                """, conn, tx);
            insert.Parameters.AddWithValue(eventId);
            insert.Parameters.AddWithValue(package.Account);
            insert.Parameters.AddWithValue(shop.ShopId);
            insert.Parameters.AddWithValue(payload);
            insert.Parameters.AddWithValue(digest);
            insert.Parameters.AddWithValue(deliveryNow);
            insert.Parameters.AddWithValue(prior.AffiliationRequestId);
            insert.Parameters.AddWithValue(prior.Account!.Revision);
            insert.Parameters.AddWithValue(nextRev);
            await insert.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static async Task<string[]> ReadEntitlementLedgerAsync(NpgsqlConnectionStringBuilder owner)
    {
        var rows = new List<string>();
        await using var conn = new NpgsqlConnection(owner.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT concat_ws('|', s.event_id, s.shop_id, s.account_revision, s.shop_revision, m.digest, encode(m.payload, 'hex'))
            FROM public.entitlement_snapshots s JOIN public.entitlement_messages m USING (event_id)
            ORDER BY s.shop_id, s.shop_revision, s.event_id
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }

    private static async Task<Dictionary<Guid, long>> ReadShopAcknowledgementsAsync(NpgsqlConnectionStringBuilder owner)
    {
        var map = new Dictionary<Guid, long>();
        await using var conn = new NpgsqlConnection(owner.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT shop_id, max(shop_revision) FROM public.applied_entitlement_shop_receipts GROUP BY shop_id
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) map[reader.GetGuid(0)] = reader.GetInt64(1);
        return map;
    }

    private static async Task<(long Revision, DateTime AccessEndsAt)> ReadMerchantLicenseStateAsync(
        NpgsqlConnectionStringBuilder merchantDb, Guid shopId)
    {
        await using var conn = new NpgsqlConnection(merchantDb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT revision, access_ends_at FROM public.shop_license_states WHERE shop_id = $1
            """, conn);
        cmd.Parameters.AddWithValue(shopId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetDateTime(1));
    }

    [Fact]
    public async Task OutOfOrderSettlementsKeepOneEffectiveBoundary()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 6000);
        var second = await RequestRefundAsync(package, order, confirmation, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, first, Instant.AddDays(4));
        var path = RequestPath(package.Account, first) + "/simulator";
        using var pending = await P0212Fixture.PostAsync(finance, path, new { outcome = "Pending" });
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        await PumpRefundsAsync(fixture);
        await ApproveEndAsync(finance, package.Account, second, Instant.AddDays(3));
        await PumpRefundsAsync(fixture);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        using var done = await P0212Fixture.PostAsync(finance, path, new { outcome = "Processed" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        await PumpRefundsAsync(fixture);
        Assert.Equal(2L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='NeedsReview'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(snapshots, await fixture.CountAsync("entitlement_snapshots"));
        var term = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal(Instant.AddDays(3), term.GetProperty("accessEndsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ConfirmedRefundLeavesReviewedAccessPendingWhenTheTermHasSinceRenewed()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        var original = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        var term = original.GetProperty("termId").GetGuid();
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        await P0308Fixture.RenewAsync(package, term, "Month");
        await P0608Tests.DrainAsync(fixture);
        var renewed = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='NeedsReview'"));
        Assert.Equal(snapshots, await fixture.CountAsync("entitlement_snapshots"));
        var after = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal(renewed.GetProperty("paidThrough").GetDateTimeOffset(), after.GetProperty("paidThrough").GetDateTimeOffset());
    }

    [Fact]
    public async Task HostedWorkerConsumesOneCommittedSend()
    {
        var settings = new Dictionary<string, string?>(P0302Fixture.SimulatorSettings)
        {
            ["Payments:ReconcileInBackground"] = "true",
        };
        await using var fixture = await P0609Tests.StartAsync(database, Instant, settings);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await fixture.CountAsync("refund_work", "state='Confirmed'") == 0)
            await Task.Delay(100, deadline.Token);
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications"));
    }

    private static async Task ApproveEndAsync(P0110CookieBrowser finance, Guid account, Guid request, DateTimeOffset end)
    {
        var preview = await PreviewAsync(finance, account, request, 4500, "EndAt", end);
        await ReadAsync(await ApproveAsync(finance, account, request, Command(preview, 4500, "EndAt", end)),
            HttpStatusCode.Created);
    }
}
