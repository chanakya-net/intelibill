using System.Text.Json;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public void LicenseNoticesContractV1MatchesWireVocabulariesPathsAndTimeVectors()
    {
        var path = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/license-notices-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("license-notices", root.GetProperty("contract").GetString());
        Assert.Equal(LicenseNoticesV1.SchemaVersion, root.GetProperty("version").GetInt32());

        var actions = root.GetProperty("actions");
        Assert.Equal(LicenseNoticesV1.ShopRead, actions.GetProperty("shopRead").GetProperty("action").GetString());
        Assert.True(MerchantBillingDelegationV1.IsActorScoped(LicenseNoticesV1.ShopRead));
        Assert.Equal(LicenseNoticesV1.AccountRead, actions.GetProperty("accountRead").GetProperty("action").GetString());
        Assert.False(MerchantBillingDelegationV1.IsActorScoped(LicenseNoticesV1.AccountRead));
        Assert.True(BillingAccountRules.Allows(BillingAccountRole.Owner, LicenseNoticesV1.AccountRead));
        Assert.True(BillingAccountRules.Allows(BillingAccountRole.Manager, LicenseNoticesV1.AccountRead));
        Assert.Equal(
            LicenseNoticesV1.RecipientAuthorityPath,
            actions.GetProperty("recipientAuthority").GetProperty("path").GetString());
        Assert.Equal(
            LicenseNoticesV1.ProofHeader,
            actions.GetProperty("recipientAuthority").GetProperty("proofHeader").GetString());

        var vocab = root.GetProperty("vocabularies");
        AssertStringSet(LicenseNoticesV1.ScopeKinds, vocab.GetProperty("scopeKinds"));
        AssertStringSet(LicenseNoticesV1.Audiences, vocab.GetProperty("audiences"));
        AssertStringSet(LicenseNoticesV1.Purposes, vocab.GetProperty("purposes"));
        AssertStringSet(LicenseNoticesV1.SourceKinds, vocab.GetProperty("sourceKinds"));
        AssertStringSet(LicenseNoticesV1.Thresholds, vocab.GetProperty("thresholds"));
        AssertStringSet(LicenseNoticesV1.Channels, vocab.GetProperty("channels"));
        AssertStringSet(LicenseNoticesV1.Statuses, vocab.GetProperty("statuses"));
        AssertStringSet(LicenseNoticesV1.GuidanceCodes, vocab.GetProperty("guidanceCodes"));
        AssertStringSet(LicenseNoticesV1.SupportedLocales, vocab.GetProperty("supportedLocales"));

        var sampleShop = new ShopLicenseNoticeItemV1(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LicenseNoticesV1.PurposeTrialReminder,
            LicenseNoticesV1.ThresholdTrialMinus3Days,
            "Active",
            LicenseNoticesV1.GuidanceTrialEnding,
            "notice.trial.minus3",
            P0209Fixture.Utc("2028-02-12T04:30:00Z"),
            P0209Fixture.Utc("2028-02-15T04:30:00Z"),
            BillingCalendar.Zone,
            "en-IN",
            "North Shop");
        using var serializedShop = JsonDocument.Parse(JsonSerializer.Serialize(sampleShop));
        foreach (var forbidden in root.GetProperty("forbiddenOperationalFields").EnumerateArray())
            Assert.False(serializedShop.RootElement.TryGetProperty(forbidden.GetString()!, out _));

        var vectors = root.GetProperty("timeVectors");
        var trialEnd = vectors.GetProperty("trialEnd").GetDateTimeOffset();
        var trialFacts = new SubscriptionAccessFacts(
            trialEnd.AddDays(-14),
            trialEnd,
            TermEvidenceState.Complete,
            SubscriptionBasis.Trial);
        var trialPlan = NotificationSchedulePolicy.PlanThresholds(trialFacts, trialEnd.AddDays(-14), false, false);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, vectors.GetProperty("trialMinus3Days").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, vectors.GetProperty("trialMinus1Day").GetDateTimeOffset()),
            ],
            trialPlan.Select(item => (item.Threshold, item.DueAt)).ToArray());

        var paidEnd = vectors.GetProperty("paidTermEnd").GetDateTimeOffset();
        var anchor = new BillingAnchor(new DateTime(2028, 1, 31, 10, 0, 0, DateTimeKind.Unspecified), BillingCalendar.Zone, BillingPeriod.Month);
        var paidFacts = new SubscriptionAccessFacts(
            BillingCalendar.TryBoundary(anchor, 0)!.Value,
            paidEnd,
            TermEvidenceState.Complete,
            SubscriptionBasis.Paid,
            anchor,
            0);
        var paidPlan = NotificationSchedulePolicy.PlanThresholds(paidFacts, paidFacts.StartsAt, false, false);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdPaidMinus7Days, vectors.GetProperty("paidMinus7Days").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdPaidMinus1Day, vectors.GetProperty("paidMinus1Day").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdGraceStart, vectors.GetProperty("graceStart").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdGraceMinus1Day, vectors.GetProperty("graceMinus1Day").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdGraceExpiry, vectors.GetProperty("graceExpiry").GetDateTimeOffset()),
            ],
            paidPlan.Select(item => (item.Threshold, item.DueAt)).ToArray());
    }

    [Fact]
    public async Task FreshSchemaMigrationEnforcesRlsProductionGrantsImmutabilityAndRollbackReplay()
    {
        var dbName = "p0701_fresh_" + Guid.NewGuid().ToString("N");
        await database.CreateEmptyLicensingDatabaseAsync(dbName);
        var migrator = database.Login(dbName, LicensingPostgreSqlFixture.MigratorRole);
        var runtime = database.Login(dbName, LicensingPostgreSqlFixture.RuntimeRole);
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(), $"""
            REVOKE ALL ON DATABASE "{dbName}" FROM PUBLIC;
            GRANT CONNECT ON DATABASE "{dbName}" TO licensing_runtime;
            """);

        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(migrator.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var context = new LicensingDbContext(options))
        {
            await context.Database.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
        }

        // Apply production-shaped runtime grants (no TRUNCATE, no schema create, explicit bounded functions only).
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            REVOKE ALL ON SCHEMA public FROM licensing_runtime;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.due_notification_planning(uuid, integer) TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.account_notification_billing_members(uuid, uuid, integer) TO licensing_runtime;
            """);

        // Verify forced RLS and unique semantic key index exist.
        await using (var conn = new NpgsqlConnection(migrator.ConnectionString))
        {
            await conn.OpenAsync();
            await using var rls = new NpgsqlCommand("""
                SELECT count(*)
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname IN ('notification_jobs', 'billing_notice_contacts', 'billing_notice_contact_events')
                  AND c.relrowsecurity
                  AND c.relforcerowsecurity
                """, conn);
            Assert.Equal(3L, (long)(await rls.ExecuteScalarAsync())!);

            await using var idx = new NpgsqlCommand("""
                SELECT count(*)
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'notification_jobs'
                  AND indexname = 'ux_notification_jobs_semantic_key'
                """, conn);
            Assert.Equal(1L, (long)(await idx.ExecuteScalarAsync())!);
        }

        var (accountId, ownerId) = await P0209Fixture.SeedAccountAsync(database.Bootstrap(dbName));
        var jobId = Guid.NewGuid();
        var dueAt = P0209Fixture.Utc("2028-02-22T04:30:00Z");

        // Unscoped runtime INSERT must be refused by RLS; scoped INSERT succeeds; UPDATE/DELETE/TRUNCATE refused!
        await using (var rt = new NpgsqlConnection(runtime.ConnectionString))
        {
            await rt.OpenAsync();

            var unscopedInsert = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO public.notification_jobs (
                        id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id,
                        audience, purpose, source_kind, source_id, source_revision, threshold,
                        due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                        status, template_key, template_arguments, planned_at, created_at)
                    VALUES (
                        $1, $2, 'Account', $2, $3, $4,
                        'CommercialBillingRole', 'PaidRenewalReminder', 'Paid', $5, 'v1:rev', 'PaidMinus7Days',
                        $6, $7, 'Asia/Kolkata', 'en-IN', ARRAY['InApp']::text[],
                        'Scheduled', 'notice.paid.minus7', '{}'::jsonb, $6, $6)
                    """, rt);
                cmd.Parameters.AddWithValue(jobId);
                cmd.Parameters.AddWithValue(accountId);
                cmd.Parameters.AddWithValue($"user:{ownerId:D}");
                cmd.Parameters.AddWithValue(ownerId);
                cmd.Parameters.AddWithValue(Guid.NewGuid());
                cmd.Parameters.AddWithValue(dueAt);
                cmd.Parameters.AddWithValue(dueAt.AddDays(7));
                await cmd.ExecuteNonQueryAsync();
            });
            Assert.Equal("42501", unscopedInsert.SqlState);

            await using (var tx = await rt.BeginTransactionAsync())
            {
                await using var scope = new NpgsqlCommand("SELECT set_config('app.billing_account_id', $1, true)", rt, tx);
                scope.Parameters.AddWithValue(accountId.ToString("D"));
                await scope.ExecuteNonQueryAsync();

                await using var insert = new NpgsqlCommand("""
                    INSERT INTO public.notification_jobs (
                        id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id,
                        audience, purpose, source_kind, source_id, source_revision, threshold,
                        due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                        status, template_key, template_arguments, planned_at, created_at)
                    VALUES (
                        $1, $2, 'Account', $2, $3, $4,
                        'CommercialBillingRole', 'PaidRenewalReminder', 'Paid', $5, 'v1:rev', 'PaidMinus7Days',
                        $6, $7, 'Asia/Kolkata', 'en-IN', ARRAY['InApp']::text[],
                        'Scheduled', 'notice.paid.minus7', '{}'::jsonb, $6, $6)
                    """, rt, tx);
                insert.Parameters.AddWithValue(jobId);
                insert.Parameters.AddWithValue(accountId);
                insert.Parameters.AddWithValue($"user:{ownerId:D}");
                insert.Parameters.AddWithValue(ownerId);
                insert.Parameters.AddWithValue(Guid.NewGuid());
                insert.Parameters.AddWithValue(dueAt);
                insert.Parameters.AddWithValue(dueAt.AddDays(7));
                Assert.Equal(1, await insert.ExecuteNonQueryAsync());
                await tx.CommitAsync();
            }

            // Unscoped SELECT returns 0 rows.
            await using (var unscopedRead = new NpgsqlCommand("SELECT count(*) FROM public.notification_jobs", rt))
                Assert.Equal(0L, (long)(await unscopedRead.ExecuteScalarAsync())!);

            // TRUNCATE and UPDATE/DELETE are denied.
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var truncate = new NpgsqlCommand("TRUNCATE public.notification_jobs", rt);
                await truncate.ExecuteNonQueryAsync();
            });
            await using (var tx = await rt.BeginTransactionAsync())
            {
                await using var scope = new NpgsqlCommand("SELECT set_config('app.billing_account_id', $1, true)", rt, tx);
                scope.Parameters.AddWithValue(accountId.ToString("D"));
                await scope.ExecuteNonQueryAsync();

                await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    await using var upd = new NpgsqlCommand(
                        "UPDATE public.notification_jobs SET status = 'Scheduled' WHERE id = $1",
                        rt,
                        tx);
                    upd.Parameters.AddWithValue(jobId);
                    await upd.ExecuteNonQueryAsync();
                });
                await tx.RollbackAsync();
            }
        }

        // Roll back PlanLicenseNotices to Baseline and replay forward cleanly.
        await using (var context = new LicensingDbContext(options))
        {
            var migratorService = context.Database.GetService<IMigrator>();
            await migratorService.MigrateAsync("20261127074702_Baseline");
            Assert.Equal(
                DBNull.Value,
                await LicensingPostgreSqlFixture.ReadScalarAsync(migrator, "SELECT to_regclass('public.notification_jobs')::text"));

            await migratorService.MigrateAsync();
            Assert.Equal(
                "notification_jobs",
                await LicensingPostgreSqlFixture.ReadScalarAsync(migrator, "SELECT to_regclass('public.notification_jobs')::text"));
        }
    }

    private static void AssertStringSet(string[] expected, JsonElement actual) =>
        Assert.Equal(expected, actual.EnumerateArray().Select(item => item.GetString()!).ToArray());
}
