using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    private const string PreviousMigration = "20261124140000_ScheduledCapacityReductions";

    [Fact]
    public async Task RefundApprovalMigrationRetainsEvidenceAndRefusesDowngrade()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);

        // Empty down/up keeps the retained P06-08 request and recreates both forced-RLS tables.
        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.False(context.Database.HasPendingModelChanges());
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            Assert.True(await fixture.ScalarAsync("SELECT to_regclass('public.refund_approvals')::text") is null or DBNull);
            Assert.True(await fixture.ScalarAsync("SELECT to_regprocedure('public.lock_refund_receipt(uuid)')::text") is null or DBNull);
            Assert.Equal(1L, await fixture.CountAsync("refund_requests"));
            await migrator.MigrateAsync();
        }
        Assert.Equal(2L, await fixture.ScalarAsync("""
            SELECT count(*) FROM pg_class WHERE relname IN ('refund_approvals', 'refund_access_decisions')
                AND relnamespace = 'public'::regnamespace AND relrowsecurity AND relforcerowsecurity
            """));
        // Runtime grants are environment-owned: recreated tables need them again.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        Assert.Equal(false, await fixture.ScalarAsync(
            "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = 'licensing_runtime'"));

        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, package.Account, request, 4500L, "Preserve");
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(preview, 4500L, "Preserve")),
            HttpStatusCode.Created);

        // A retained approval refuses downgrade under the non-superuser migration identity.
        await using (var context = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            Assert.Equal("off", await context.Database.SqlQueryRaw<string>(
                "SELECT current_setting('is_superuser') AS \"Value\"").SingleAsync());
            var refused = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused?.GetBaseException()).SqlState);
        }
        foreach (var statement in new[]
        {
            "UPDATE public.refund_approvals SET amount_paise = 1", "DELETE FROM public.refund_approvals",
            "TRUNCATE public.refund_approvals CASCADE", "UPDATE public.refund_access_decisions SET treatment = 'EndAt'",
            "DELETE FROM public.refund_access_decisions", "TRUNCATE public.refund_access_decisions CASCADE",
        })
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement));
            Assert.Equal("55000", refused.SqlState);
        }
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", "amount_paise = 4500"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", "treatment = 'Preserve'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_requests", "amount_paise = 6000"));
        await RecordAsync("migration-and-immutability", new
        {
            emptyDownThenUpKeepsRefundRequests = 1, forcedRlsTables = 2, runtimeRoleSuperuserOrBypassRls = false,
            retainedApprovalDowngradeSqlState = "55000", updateDeleteTruncateSqlState = "55000", statementsRefused = 6,
            migrationIdentitySuperuser = "off", approvalsRetained = 1, decisionsRetained = 1,
        });
    }

    [Fact]
    public async Task DirectSqlContendersCannotExceedTheReceiptOrSkipTheDecision()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 8000L);
        var second = await RequestRefundAsync(package, order, confirmation, 7000L);
        var other = await P0209Fixture.CreateAccountAsync(package.Payer.Client, "Unrelated Account");
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await using var one = await SqlScope.OpenAsync(fixture, package.Account);
        await using var two = await SqlScope.OpenAsync(fixture, package.Account);

        // Two independent runtime transactions, no account fence: the second waits on the first's receipt lock.
        var held = await one.ApproveAsync(first, 8000L, 12000L, 1);
        var waiting = two.ApproveAsync(second, 7000L, 12000L, 1);
        Assert.True(await one.BlocksAsync(two), "The second writer did not wait on the receipt lock.");

        // An amount without its access decision cannot commit; its hold disappears with it.
        var incomplete = await Assert.ThrowsAsync<PostgresException>(() => one.CommitAsync());
        Assert.Equal("23514", incomplete.SqlState);
        var approval = await waiting;
        await two.DecideAsync(approval, second);
        await two.CommitAsync();
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", $"approval_id = '{approval:D}' AND amount_paise = 7000"));
        Assert.Equal(0L, await fixture.CountAsync("refund_approvals", $"approval_id = '{held:D}'"));

        // With 5,000 left, 8,000 is refused whether the writer states the ledger honestly or not.
        foreach (var (before, revision) in new[] { (5000L, 2), (12000L, 2), (12000L, 1) })
        {
            await using var liar = await SqlScope.OpenAsync(fixture, package.Account);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => liar.ApproveAsync(first, 8000L, before, revision));
            Assert.Equal("23514", refused.SqlState);
        }

        // Another account's scope sees neither table and cannot insert into this account.
        await using var foreign = await SqlScope.OpenAsync(fixture, other);
        Assert.Equal(0L, await foreign.CountAsync("refund_approvals"));
        Assert.Equal(0L, await foreign.CountAsync("refund_access_decisions"));
        await foreign.ApproveAsync(first, 1000L, 5000L, 2, rows: 0);
        // Without a provider admin scope the private amount evidence is hidden; the operational decision is not.
        await using var member = await SqlScope.OpenAsync(fixture, package.Account, admin: false);
        Assert.Equal(0L, await member.CountAsync("refund_approvals"));
        Assert.Equal(1L, await member.CountAsync("refund_access_decisions"));
        var unscoped = await Assert.ThrowsAsync<PostgresException>(() => member.ApproveAsync(first, 1000L, 12000L, 1));
        Assert.Equal("42501", unscoped.SqlState);
        Assert.Equal(7000L, (decimal)(await fixture.ScalarAsync("SELECT sum(amount_paise) FROM public.refund_approvals"))!);
        await RecordAsync("direct-sql-contenders", new
        {
            role = "licensing_runtime", accountFenceTaken = false, secondWriterBlockedByFirst = true,
            amountWithoutDecisionCommitSqlState = incomplete.SqlState, survivingApprovalPaise = 7000,
            overCeilingOrMisstatedInsertSqlState = "23514", attempts = 3, otherAccountVisibleApprovals = 0,
            otherAccountVisibleDecisions = 0, memberScopeVisibleApprovals = 0, memberScopeVisibleDecisions = 1,
            memberScopeInsertSqlState = unscoped.SqlState, reservedTotalPaise = 7000,
        });
    }

    [Fact]
    public async Task ConfirmedPlusHeldCountsEachReservationOnce()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 4500L);
        var second = await RequestRefundAsync(package, order, confirmation, 6000L);
        var third = await RequestRefundAsync(package, order, confirmation, 1500L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        foreach (var (request, paise) in new[] { (first, 4500L), (second, 6000L) })
            await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(
                await PreviewAsync(billing, package.Account, request, paise, "Preserve"), paise, "Preserve")), HttpStatusCode.Created);

        // 12,000 verified, 4,500 and 6,000 held: the production balance leaves 1,500.
        var before = await PreviewAsync(billing, package.Account, third, 1500L, "Preserve");
        Assert.Equal(10500L, before.GetProperty("occupiedAmountPaise").GetInt64());
        Assert.Equal(1500L, before.GetProperty("remainingAmountPaise").GetInt64());

        // Test-only observation keyed by reservation id: classify the 4,500 hold as confirmed. No provider outcome
        // is observed in P06-09; this only proves the balance counts each stable reservation identity once.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            CREATE TABLE public.p0609_observations AS
            SELECT reservation_id, CASE WHEN amount_paise = 4500 THEN 'Confirmed' ELSE 'Held' END AS state
            FROM public.refund_approvals;
            """);
        Assert.Equal("4500|6000|2|2", await fixture.ScalarAsync("""
            SELECT concat_ws('|', sum(a.amount_paise) FILTER (WHERE o.state = 'Confirmed'),
                sum(a.amount_paise) FILTER (WHERE o.state = 'Held'), count(*), count(DISTINCT a.reservation_id))
            FROM public.refund_approvals a JOIN public.p0609_observations o USING (reservation_id)
            """));
        var after = await PreviewAsync(billing, package.Account, third, 1500L, "Preserve");
        Assert.Equal(1500L, after.GetProperty("remainingAmountPaise").GetInt64());
        Assert.Equal(10500L, (long)(await fixture.ScalarAsync(
            $"SELECT public.refund_receipt_occupied_paise('{package.Account:D}', '{confirmation:D}')"))!);

        // The ceiling is enforced by real writes: 1,500 fits exactly, nothing more does.
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, third, 1501L, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");
        var last = await ReadAsync(await ApproveAsync(billing, package.Account, third, Command(after, 1500L, "Preserve")),
            HttpStatusCode.Created);
        Assert.Equal(0L, last.GetProperty("remainingAfterPaise").GetInt64());
        Assert.Equal(3L, await fixture.CountAsync("refund_approvals"));
        await RecordAsync("confirmed-plus-held", new
        {
            verifiedPaise = 12000, classifiedConfirmedPaise = 4500, heldPaise = 6000, reservations = 2,
            distinctReservationIds = 2, remainingBeforeClassificationPaise = 1500, remainingAfterClassificationPaise = 1500,
            occupiedFunctionPaise = 10500, note = "Test-only classification keyed by reservation id; no provider confirmation is observed in P06-09.",
        });
    }

    /// <summary>One runtime-role transaction scoped like the provider admin scope, without the account fence.</summary>
    private sealed class SqlScope(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account,
        Guid adminId, Guid sessionId, DateTime mfaVerifiedAt) : IAsyncDisposable
    {
        internal static async Task<SqlScope> OpenAsync(P0308Fixture fixture, Guid account, bool admin = true, Guid? actorOverride = null, Guid? session = null)
        {
            var latest = session is { } id ? $"FROM public.admin_sessions WHERE id='{id:D}'"
                : "FROM public.admin_sessions ORDER BY mfa_verified_at DESC LIMIT 1";
            var adminId = (Guid)(await fixture.ScalarAsync($"SELECT admin_id {latest}"))!;
            var sessionId = (Guid)(await fixture.ScalarAsync($"SELECT id {latest}"))!;
            var mfa = (DateTime)(await fixture.ScalarAsync($"SELECT mfa_verified_at {latest}"))!;
            var connection = new NpgsqlConnection(fixture.Hosts.Database.Login(
                fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime").ConnectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var scope = new NpgsqlCommand("""
                SELECT set_config('app.billing_account_id', $1, true), set_config('app.manual_admin_id', $2, true),
                    set_config('app.manual_session_id', $3, true)
                """, connection, transaction);
            foreach (var value in new[] { account.ToString("D"), admin ? (actorOverride ?? adminId).ToString("D") : "", admin ? sessionId.ToString("D") : "" })
                scope.Parameters.AddWithValue(value);
            await scope.ExecuteNonQueryAsync();
            return new(connection, transaction, account, adminId, sessionId, mfa);
        }

        /// <summary>Inserts an amount row stating its own ledger figures; returns its id once the insert completes.</summary>
        internal async Task<Guid> ApproveAsync(Guid request, long paise, long remainingBefore, int revision, int rows = 1)
        {
            var id = Guid.NewGuid();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO public.refund_approvals (account_id, approval_id, reservation_id, refund_request_id, order_id,
                    confirmation_id, attempt_id, manual_receipt_id, operation_key, payload_hash, impact_digest, admin_id,
                    session_id, mfa_verified_at, authorized_at, decided_at, provider_id, provider_account_id, environment,
                    configuration_version, refund_policy_version, payment_amount_paise, requested_amount_paise, amount_paise,
                    remaining_before_paise, remaining_after_paise, receipt_revision, currency, reason)
                SELECT $1, $2, gen_random_uuid(), r.refund_request_id, r.order_id, r.confirmation_id, r.attempt_id,
                    r.manual_receipt_id, gen_random_uuid(), repeat('a', 64), repeat('b', 64), $7, $8, $9, $9,
                    r.requested_at, r.provider_id, r.provider_account_id, r.environment, r.configuration_version,
                    coalesce(c.facts->'terms'->>'refundPolicyVersion', 'unrecorded'), r.payment_amount_paise,
                    r.amount_paise, $3, $4, $4 - $3, $5, r.currency, 'Direct SQL contender'
                FROM public.refund_requests r JOIN public.payment_confirmations c USING (confirmation_id)
                WHERE r.refund_request_id = $6
                """, connection, transaction);
            foreach (var value in new object[] { account, id, paise, remainingBefore, (long)revision, request, adminId,
                sessionId, DateTime.SpecifyKind(mfaVerifiedAt, DateTimeKind.Utc) })
                insert.Parameters.AddWithValue(value);
            Assert.Equal(rows, await insert.ExecuteNonQueryAsync());
            return id;
        }

        internal async Task DecideAsync(Guid approval, Guid request)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO public.refund_access_decisions (account_id, decision_id, approval_id, refund_request_id,
                    treatment, term_id, previous_paid_through, previous_access_ends_at, new_paid_through,
                    new_access_ends_at, shop_count, shops, decided_at)
                SELECT a.account_id, gen_random_uuid(), a.approval_id, a.refund_request_id, 'Preserve', t.term_id,
                    t.ends_at, t.ends_at + interval '168 hours', t.ends_at, t.ends_at + interval '168 hours',
                    scope.count, scope.shops, a.decided_at
                FROM public.refund_approvals a JOIN public.payment_fulfilments f USING (account_id, order_id)
                JOIN public.subscription_coverage_terms t USING (account_id, term_id)
                CROSS JOIN LATERAL (SELECT count(*)::int, jsonb_agg(jsonb_build_object('shopId', x.shop_id,
                    'shopName', x.shop_name, 'restricted', x.restricted, 'closed', x.closed,
                    'scheduledRestrictionAt', x.scheduled_restriction_at, 'courtesyEndsAt', x.courtesy_ends_at)
                    ORDER BY x.shop_id) AS shops FROM public.refund_target_shops(a.account_id,t.term_id,a.decided_at) x) scope
                WHERE a.approval_id = $1 AND a.refund_request_id = $2
                """, connection, transaction);
            insert.Parameters.AddWithValue(approval);
            insert.Parameters.AddWithValue(request);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        internal async Task EndAsync(Guid approval, Guid term, long revision, DateTimeOffset paid,
            DateTimeOffset until, DateTimeOffset end, string shops)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO public.refund_access_decisions(account_id, decision_id, approval_id, refund_request_id,
                    treatment, term_id, term_revision, previous_paid_through, previous_access_ends_at,
                    effective_at, new_paid_through, new_access_ends_at, shop_count, shops, decided_at)
                SELECT a.account_id, gen_random_uuid(), a.approval_id, a.refund_request_id, 'EndAt', $2, $3,
                    $4, $5, $6, least($6,$4), $6, jsonb_array_length($7::jsonb), $7::jsonb, a.decided_at
                FROM public.refund_approvals a WHERE a.approval_id=$1
                """, connection, transaction);
            foreach (var value in new object[] { approval, term, revision, paid, until, end, shops })
                insert.Parameters.AddWithValue(value);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        internal async Task<long> CountAsync(string table)
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM public.{table}", connection, transaction);
            return (long)(await count.ExecuteScalarAsync())!;
        }

        /// <summary>True once PostgreSQL reports <paramref name="other"/> blocked by this transaction.</summary>
        internal async Task<bool> BlocksAsync(SqlScope other)
        {
            for (var attempt = 0; attempt < 150; attempt++)
            {
                await using var probe = new NpgsqlCommand("SELECT pg_backend_pid() = ANY(pg_blocking_pids($1))", connection, transaction);
                probe.Parameters.AddWithValue(other.Pid);
                if ((bool)(await probe.ExecuteScalarAsync())!) return true;
                await Task.Delay(20);
            }
            return false;
        }

        private int Pid => connection.ProcessID;

        internal Task CommitAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
