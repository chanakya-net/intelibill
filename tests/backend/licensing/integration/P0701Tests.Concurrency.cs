using System.Diagnostics;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task ConcurrentPlannersRetainOneSemanticIntentPerRecipient()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            settings: new Dictionary<string, string?>(P0302Fixture.SimulatorSettings)
            {
                ["Database:MaxPoolSize"] = "6",
                ["Notifications:PlanInBackground"] = "false",
            });

        // Grouped 2-shop paid package: 1 account-scope commercial job per recipient per threshold + shop-scope operational jobs per shop.
        var package = await paid.PackageAsync(2, ["Branch One", "Branch Two"]);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        var staff1 = await SignInAsync(paid.Hosts, ShopRole.Staff, package.Shops[0].ShopId);
        var staff2 = await SignInAsync(paid.Hosts, ShopRole.Staff, package.Shops[1].ShopId);
        var payerMembershipId = (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            paid.Hosts.LicensingOwner,
            $"SELECT id FROM public.billing_account_members WHERE account_id = '{package.Account:D}' AND merchant_user_id = '{package.Payer.UserId:D}'"))!;

        // Two independent planner scopes + connections converge while a DB barrier holds the membership row lock.
        await using var gate = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var monitor = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await monitor.OpenAsync();

        int[] observedPids;
        await using (var barrier = await gate.BeginTransactionAsync())
        {
            await using (var hold = new NpgsqlCommand(
                "SELECT id FROM public.billing_account_members WHERE id = $1 FOR NO KEY UPDATE",
                gate,
                barrier))
            {
                hold.Parameters.AddWithValue(payerMembershipId);
                Assert.Equal(payerMembershipId, await hold.ExecuteScalarAsync());
            }

            var pass1 = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            var pass2 = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            observedPids = await WaitForTwoConcurrentBackendsAsync(monitor, pass1, pass2);

            // A third concurrent writer rolls back before the two planners proceed.
            await using (var rollbackConn = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString))
            {
                await rollbackConn.OpenAsync();
                await using var rolled = await rollbackConn.BeginTransactionAsync();
                await using var insertAndRollback = new NpgsqlCommand("""
                    INSERT INTO public.notification_jobs (
                        id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id,
                        audience, purpose, source_kind, source_id, source_revision, threshold,
                        due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                        status, template_key, template_arguments, planned_at, created_at)
                    VALUES (
                        $1, $2, 'Account', $2, $3, $4,
                        'CommercialBillingRole', 'PaidRenewalReminder', 'Paid', $5, 'v1:rolledback', 'PaidMinus7Days',
                        $6, $7, 'Asia/Kolkata', 'en-IN', ARRAY['InApp']::text[],
                        'Scheduled', 'notice.paid.minus7', '{}'::jsonb, $6, $6)
                    """, rollbackConn, rolled);
                insertAndRollback.Parameters.AddWithValue(Guid.NewGuid());
                insertAndRollback.Parameters.AddWithValue(package.Account);
                insertAndRollback.Parameters.AddWithValue($"user:{package.Payer.UserId:D}");
                insertAndRollback.Parameters.AddWithValue(package.Payer.UserId);
                insertAndRollback.Parameters.AddWithValue(Guid.NewGuid());
                insertAndRollback.Parameters.AddWithValue(Utc("2028-02-22T04:30:00Z"));
                insertAndRollback.Parameters.AddWithValue(Utc("2028-02-29T04:30:00Z"));
                Assert.Equal(1, await insertAndRollback.ExecuteNonQueryAsync());
                await rolled.RollbackAsync();
            }

            await barrier.RollbackAsync();
            var results = await Task.WhenAll(pass1, pass2);
            // Total unique rows across the grouped package:
            // Account scope: 1 payer * 5 thresholds = 5
            // Shop 1 scope: payer + staff1 = 2 * 5 = 10
            // Shop 2 scope: shop2Owner + staff2 = 2 * 5 = 10
            // Combined inserted across both racing passes MUST equal 25, with duplicates absorbing the race!
            Assert.Equal(25, results[0].Inserted + results[1].Inserted);
            Assert.Equal(25, results[0].SkippedDuplicate + results[1].SkippedDuplicate);
        }

        Assert.Equal(2, observedPids.Distinct().Count());
        var baselineRows = await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account);
        Assert.Equal(25, baselineRows.Count);
        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, $"user:{package.Payer.UserId:D}")).Count);
        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeShop, $"user:{staff1.UserId:D}")).Count);
        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeShop, $"user:{staff2.UserId:D}")).Count);

        // Duplicate replay pass + locale/email alias change outside semantic key retain the exact original rows.
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Checkout.Delivery.MerchantDb,
            $"UPDATE users SET language = 'ta-IN' WHERE id = '{package.Payer.UserId:D}'");
        var replay = await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0, replay.Inserted);
        Assert.Equal(25, replay.SkippedDuplicate);
        Assert.Equal(baselineRows, await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account));

        // Routine EntitlementPublisher heartbeat increments shop snapshot revision but leaves notification source_revision unchanged.
        await using (var scope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicensingDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IEntitlementPublisher>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_account_id', {package.Account.ToString("D")}, true)");
            await publisher.PublishAsync(package.Account, package.Shops[0].ShopId, CancellationToken.None);
            await tx.CommitAsync();
        }

        var afterHeartbeat = await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0, afterHeartbeat.Inserted);
        Assert.Equal(baselineRows, await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account));

        // Simulated restart (fresh scoped planner pass against persisted DB state) keeps identical rows.
        await using (var restartScope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var restartedPlanner = restartScope.ServiceProvider.GetRequiredService<NotificationPlanner>();
            var restarted = await restartedPlanner.PlanAllDueAsync(CancellationToken.None);
            Assert.Equal(0, restarted.Inserted);
            Assert.Equal(25, restarted.SkippedDuplicate);
        }

        Assert.Equal(baselineRows, await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account));
    }

    private static async Task<int[]> WaitForTwoConcurrentBackendsAsync(
        NpgsqlConnection monitor,
        Task<NotificationPassResult> pass1,
        Task<NotificationPassResult> pass2)
    {
        await using var sql = new NpgsqlCommand("""
            SELECT pid
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND usename = 'licensing_runtime'
              AND wait_event_type = 'Lock'
              AND query LIKE '%billing_account_members%FOR SHARE%'
            ORDER BY pid
            """, monitor);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            var pids = new List<int>();
            await using (var reader = await sql.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    pids.Add(reader.GetInt32(0));
            }

            if (pids.Count >= 2)
                return [.. pids];
            Assert.False(pass1.IsCompleted && pass2.IsCompleted, "Both planners completed before lock overlap was observed.");
            await Task.Delay(15);
        }

        Assert.Fail("Expected two distinct PostgreSQL backend PIDs waiting concurrently on billing_account_members FOR SHARE.");
        return [];
    }

    private static async Task<Dictionary<Guid, DateTimeOffset>> ReadAllJobIdsAndCreatedAtAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, created_at FROM public.notification_jobs WHERE account_id = $1 ORDER BY id",
            conn);
        cmd.Parameters.AddWithValue(accountId);
        var map = new Dictionary<Guid, DateTimeOffset>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            map[reader.GetGuid(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        return map;
    }
}
