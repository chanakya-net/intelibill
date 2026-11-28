using System.Collections.Concurrent;
using System.Diagnostics;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Domain.Entities;
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

        var package = await paid.PackageAsync(2, ["Branch One", "Branch Two"]);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        var staff1 = await SignInAsync(paid.Hosts, ShopRole.Staff, package.Shops[0].ShopId);
        var staff2 = await SignInAsync(paid.Hosts, ShopRole.Staff, package.Shops[1].ShopId);

        var observedPids = await RunTwoPlannerRaceWithRollbackAsync(paid, package);
        Assert.Equal(2, observedPids.Distinct().Count());

        var baselineRows = await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account);
        Assert.Equal(25, baselineRows.Count);
        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, $"user:{package.Payer.UserId:D}")).Count);
        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeShop, $"user:{staff1.UserId:D}")).Count);
        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeShop, $"user:{staff2.UserId:D}")).Count);

        await AssertReplayHeartbeatAndFreshScopeInvarianceAsync(paid, package, baselineRows);
    }

    [Fact]
    public async Task RoleRemovalRaceWithEnqueueBlocksRevokedCommercialRecipient()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            settings: new Dictionary<string, string?>(P0302Fixture.SimulatorSettings)
            {
                ["Database:MaxPoolSize"] = "4",
                ["Notifications:PlanInBackground"] = "false",
            });
        var package = await paid.PackageAsync(1, ["Race Store"]);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        await using var gate = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var monitor = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await monitor.OpenAsync();

        // Control: hold FOR NO KEY UPDATE, observe planner FOR SHARE wait, rollback -> scheduled.
        var controlManager = await SignInAsync(paid.Hosts);
        var controlMembership = await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, package.Payer.Client, package.Account, controlManager);
        await using (var barrier = await gate.BeginTransactionAsync())
        {
            await HoldMemberRowAsync(barrier, controlMembership);
            var planTask = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            await WaitForMemberShareLockAsync(monitor, planTask);
            await barrier.RollbackAsync();
            Assert.True((await planTask).Inserted >= 5);
        }

        Assert.Equal(5, (await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, $"user:{controlManager.UserId:D}")).Count);

        // Race: hold FOR NO KEY UPDATE, observe FOR SHARE wait, commit removal -> 0 rows.
        var revokedManager = await SignInAsync(paid.Hosts);
        var revokedMembership = await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, package.Payer.Client, package.Account, revokedManager);
        await using (var barrier = await gate.BeginTransactionAsync())
        {
            await HoldMemberRowAsync(barrier, revokedMembership);
            var planTask = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            await WaitForMemberShareLockAsync(monitor, planTask);
            await CommitMemberRemovalAsync(gate, barrier, revokedMembership);
            Assert.True((await planTask).SkippedAuthorityChanged >= 5);
        }

        Assert.Empty(await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, $"user:{revokedManager.UserId:D}"));
    }

    [Fact]
    public async Task CommitPreconditionsRejectStaleDeadlineOrAffiliationChangedDuringHttpGap()
    {
        var trialStart = Utc("2028-02-01T04:30:00Z");
        var trialClock = new P0201Clock { Now = trialStart };
        var gap = new ObservationCommitGapBarrier();
        await using var delivery = await P0213Fixture.StartAsync(
            database,
            businessClock: trialClock,
            licensingServices: services => WrapAuthority(services, inner => new BarrierNoticeAuthority(inner, gap)));
        var trial = delivery.Trial;
        var shopOwner = await SignInAsync(trial.Hosts, ShopRole.Owner);
        var payer = await SignInAsync(trial.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await P0213Fixture.LinkAsync(account, payer, shopOwner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shopOwner, trial.Accept())).Status);

        // Start planner; pause during merchant HTTP lookup (between ReadAccountPlanContextAsync and CommitPlannedJobsAsync).
        gap.ArmNext();
        var stalePass = RunPlannerPassAsync(trial.Hosts.Licensing.Services);
        await gap.EnteredHttp.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Commit a trial extension on an independent connection during the HTTP gap (moves deadline Feb 15 -> Feb 25).
        await ExtendTrialAsync(trial.CatalogueManager!, account, shopOwner.ShopId, Utc("2028-02-25T04:30:00Z"));
        gap.ReleaseHttp.TrySetResult();

        var staleResult = await stalePass;
        Assert.Equal(0, staleResult.Inserted);
        Assert.True(staleResult.SkippedAuthorityChanged >= 4);
        Assert.Equal(0L, await CountJobsForAccountAsync(trial.LicensingOwner, account));

        // Subsequent pass observes the extended deadline and inserts ONLY the new Feb 22 / Feb 24 keys.
        var freshResult = await RunPlannerPassAsync(trial.Hosts.Licensing.Services);
        Assert.True(freshResult.Inserted >= 4);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-22T04:30:00Z")),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-24T04:30:00Z")),
            ],
            await ReadScheduledThresholdsAsync(trial.LicensingOwner, account, LicenseNoticesV1.ScopeAccount, $"user:{payer.UserId:D}"));
    }

    [Fact]
    public async Task BoundedDiscoverySkipsExpiredOnlyAccountsWithoutMerchantTrafficAndPagesCandidates()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        var callCounts = new ConcurrentDictionary<Guid, int>();
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            services: services => WrapAuthority(services, inner => new CountingNoticeAuthority(inner, callCounts)));

        // Expired-only account (paid term ending Feb 29, grace ending Mar 7 -> expired by Mar 10).
        var expiredPkg = await paid.PackageAsync(1, ["Expired Store"]);
        await PurchaseAsync(expiredPkg, "Month");
        await P0608Tests.DrainAsync(paid);

        // Two active accounts starting Mar 10 so multiple discovery pages exist.
        paid.At(Utc("2028-03-10T04:30:00Z"));
        var activeA = await paid.PackageAsync(1, ["Active Store A"]);
        await PurchaseAsync(activeA, "Month");
        var activeB = await paid.PackageAsync(1, ["Active Store B"]);
        await PurchaseAsync(activeB, "Month");
        await P0608Tests.DrainAsync(paid);

        callCounts.Clear();
        await using var scope = paid.Hosts.Licensing.Services.CreateAsyncScope();
        var planner = scope.ServiceProvider.GetRequiredService<NotificationPlanner>();

        // Tick 1: maxAccounts=1 handles first eligible account; Tick 2 progresses via cursor to second eligible account.
        var tick1 = await planner.PlanBatchAsync(null, maxAccounts: 1, accountPageSize: 1, termPageSize: 50, memberPageSize: 50, CancellationToken.None);
        Assert.Equal(1, tick1.Pass.AccountsScanned);
        Assert.NotNull(tick1.NextAfterAccount);

        var tick2 = await planner.PlanBatchAsync(tick1.NextAfterAccount, maxAccounts: 1, accountPageSize: 1, termPageSize: 50, memberPageSize: 50, CancellationToken.None);
        Assert.Equal(1, tick2.Pass.AccountsScanned);

        // Zero merchant recipient HTTP calls were ever issued for the expired-only account!
        Assert.False(callCounts.ContainsKey(expiredPkg.Account));
        Assert.Equal(0L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, expiredPkg.Account));
        Assert.Equal(10L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, activeA.Account));
        Assert.Equal(10L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, activeB.Account));

        // Newly added billing manager on activeA is discovered on wrap-around without duplicating existing rows.
        var newManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, activeA.Payer.Client, activeA.Account, newManager);
        var concurrent = await Task.WhenAll(
            RunPlannerPassAsync(paid.Hosts.Licensing.Services, accountPageSize: 1),
            RunPlannerPassAsync(paid.Hosts.Licensing.Services, accountPageSize: 1));
        Assert.Equal(5, concurrent[0].Inserted + concurrent[1].Inserted);
        Assert.Equal(15L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, activeA.Account));
        Assert.False(callCounts.ContainsKey(expiredPkg.Account));
    }

    private static async Task<int[]> RunTwoPlannerRaceWithRollbackAsync(P0308Fixture paid, P0308Fixture.Package package)
    {
        var payerMembershipId = (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            paid.Hosts.LicensingOwner,
            $"SELECT id FROM public.billing_account_members WHERE account_id = '{package.Account:D}' AND merchant_user_id = '{package.Payer.UserId:D}'"))!;
        await using var gate = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var monitor = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await monitor.OpenAsync();

        await using var barrier = await gate.BeginTransactionAsync();
        await HoldMemberRowAsync(barrier, payerMembershipId);
        var pass1 = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var pass2 = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var observedPids = await WaitForTwoConcurrentBackendsAsync(monitor, pass1, pass2);

        await InsertAndRollbackProbeJobAsync(paid.Hosts.LicensingOwner, package.Account, package.Payer.UserId);
        await barrier.RollbackAsync();

        var results = await Task.WhenAll(pass1, pass2);
        Assert.Equal(25, results[0].Inserted + results[1].Inserted);
        Assert.Equal(25, results[0].SkippedDuplicate + results[1].SkippedDuplicate);
        return observedPids;
    }

    private static async Task AssertReplayHeartbeatAndFreshScopeInvarianceAsync(
        P0308Fixture paid,
        P0308Fixture.Package package,
        Dictionary<Guid, DateTimeOffset> baselineRows)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Checkout.Delivery.MerchantDb,
            $"UPDATE users SET language = 'ta-IN' WHERE id = '{package.Payer.UserId:D}'");
        var replay = await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0, replay.Inserted);
        Assert.Equal(25, replay.SkippedDuplicate);
        Assert.Equal(baselineRows, await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account));

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

        Assert.Equal(0, (await RunPlannerPassAsync(paid.Hosts.Licensing.Services)).Inserted);
        await using (var freshScope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var fresh = await freshScope.ServiceProvider.GetRequiredService<NotificationPlanner>().PlanAllDueAsync(CancellationToken.None);
            Assert.Equal(0, fresh.Inserted);
            Assert.Equal(25, fresh.SkippedDuplicate);
        }

        Assert.Equal(baselineRows, await ReadAllJobIdsAndCreatedAtAsync(paid.Hosts.LicensingOwner, package.Account));
    }

    private static async Task InsertAndRollbackProbeJobAsync(NpgsqlConnectionStringBuilder settings, Guid accountId, Guid userId)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand("""
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
            """, conn, tx);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue($"user:{userId:D}");
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(Utc("2028-02-22T04:30:00Z"));
        cmd.Parameters.AddWithValue(Utc("2028-02-29T04:30:00Z"));
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        await tx.RollbackAsync();
    }

    private static async Task HoldMemberRowAsync(NpgsqlTransaction barrier, Guid membershipId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM billing_account_members WHERE id = $1 FOR NO KEY UPDATE",
            barrier.Connection,
            barrier);
        cmd.Parameters.AddWithValue(membershipId);
        Assert.Equal(membershipId, await cmd.ExecuteScalarAsync());
    }

    private static async Task CommitMemberRemovalAsync(NpgsqlConnection gate, NpgsqlTransaction barrier, Guid membershipId)
    {
        await using var remove = new NpgsqlCommand(
            "UPDATE billing_account_members SET status = $2, revision = revision + 1 WHERE id = $1",
            gate,
            barrier);
        remove.Parameters.AddWithValue(membershipId);
        remove.Parameters.AddWithValue((int)BillingAccountMemberStatus.Removed);
        Assert.Equal(1, await remove.ExecuteNonQueryAsync());
        await barrier.CommitAsync();
    }

    private static async Task WaitForMemberShareLockAsync(NpgsqlConnection monitor, Task<NotificationPassResult> pass)
    {
        await using var sql = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND usename = 'licensing_runtime'
              AND wait_event_type = 'Lock'
              AND query LIKE '%billing_account_members%FOR SHARE%'
            """, monitor);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            if ((long)(await sql.ExecuteScalarAsync())! >= 1L)
                return;
            Assert.False(pass.IsCompleted, "Planner finished before waiting on billing_account_members FOR SHARE.");
            await Task.Delay(15);
        }

        Assert.Fail("Timed out waiting for planner FOR SHARE lock on billing_account_members.");
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
        await using var cmd = new NpgsqlCommand("SELECT id, created_at FROM public.notification_jobs WHERE account_id = $1 ORDER BY id", conn);
        cmd.Parameters.AddWithValue(accountId);
        var map = new Dictionary<Guid, DateTimeOffset>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            map[reader.GetGuid(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        return map;
    }

    private static void WrapAuthority(
        IServiceCollection services,
        Func<IMerchantNoticeRecipientAuthority, IMerchantNoticeRecipientAuthority> wrap)
    {
        var original = services.Last(d => d.ServiceType == typeof(IMerchantNoticeRecipientAuthority));
        services.Remove(original);
        services.AddScoped(provider =>
        {
            var inner = (IMerchantNoticeRecipientAuthority)(original.ImplementationType is { } impl
                ? ActivatorUtilities.CreateInstance(provider, impl)
                : original.ImplementationFactory!(provider));
            return wrap(inner);
        });
    }

    private sealed class ObservationCommitGapBarrier
    {
        private int _armed;
        public TaskCompletionSource EnteredHttp { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseHttp { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ArmNext()
        {
            EnteredHttp = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ReleaseHttp = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _armed, 1);
        }

        public bool TryConsume() => Interlocked.Exchange(ref _armed, 0) == 1;
    }

    private sealed class BarrierNoticeAuthority(
        IMerchantNoticeRecipientAuthority inner,
        ObservationCommitGapBarrier barrier) : IMerchantNoticeRecipientAuthority
    {
        public bool Enabled => inner.Enabled;

        public async Task<NoticeRecipientAuthorityResponseV1?> InspectAsync(
            NoticeRecipientAuthorityRequestV1 request,
            CancellationToken ct)
        {
            var result = await inner.InspectAsync(request, ct);
            if (barrier.TryConsume())
            {
                barrier.EnteredHttp.TrySetResult();
                await barrier.ReleaseHttp.Task.WaitAsync(ct);
            }

            return result;
        }
    }

    private sealed class CountingNoticeAuthority(
        IMerchantNoticeRecipientAuthority inner,
        ConcurrentDictionary<Guid, int> counts) : IMerchantNoticeRecipientAuthority
    {
        public bool Enabled => inner.Enabled;

        public Task<NoticeRecipientAuthorityResponseV1?> InspectAsync(
            NoticeRecipientAuthorityRequestV1 request,
            CancellationToken ct)
        {
            counts.AddOrUpdate(request.AccountId, 1, (_, current) => current + 1);
            return inner.InspectAsync(request, ct);
        }
    }
}
