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
        AssertActionsAndVocabularies(root);
        AssertForbiddenOperationalAndContactFields(root);
        AssertContractTimeVectors(root.GetProperty("timeVectors"));
        AssertRecipientAuthorityResponseValidation();
    }

    [Fact]
    public async Task FreshSchemaMigrationEnforcesRlsProductionGrantsDiscoveryIsolationAndRollbackReplay()
    {
        var dbName = "p0701_fresh_" + Guid.NewGuid().ToString("N");
        await database.CreateEmptyLicensingDatabaseAsync(dbName);
        var migrator = database.Login(dbName, LicensingPostgreSqlFixture.MigratorRole);
        var runtime = database.Login(dbName, LicensingPostgreSqlFixture.RuntimeRole);
        await ApplyFreshMigrationAndProductionGrantsAsync(dbName, migrator);

        await AssertForcedRlsAndUniqueIndexesAsync(migrator);
        var (accountId, ownerId) = await P0209Fixture.SeedAccountAsync(database.Bootstrap(dbName));
        await AssertRuntimeRlsAndImmutabilityAsync(runtime, accountId, ownerId);
        await AssertDiscoveryIsolationAcrossScopesAsync(database.Bootstrap(dbName), runtime, accountId);
        await AssertRollbackAndReplayAsync(migrator);
    }

    private static void AssertActionsAndVocabularies(JsonElement root)
    {
        var actions = root.GetProperty("actions");
        Assert.Equal(LicenseNoticesV1.ShopRead, actions.GetProperty("shopRead").GetProperty("action").GetString());
        Assert.True(MerchantBillingDelegationV1.IsActorScoped(LicenseNoticesV1.ShopRead));
        Assert.Equal(LicenseNoticesV1.AccountRead, actions.GetProperty("accountRead").GetProperty("action").GetString());
        Assert.False(MerchantBillingDelegationV1.IsActorScoped(LicenseNoticesV1.AccountRead));
        Assert.True(BillingAccountRules.Allows(BillingAccountRole.Owner, LicenseNoticesV1.AccountRead));
        Assert.True(BillingAccountRules.Allows(BillingAccountRole.Manager, LicenseNoticesV1.AccountRead));
        Assert.Equal(LicenseNoticesV1.RecipientAuthorityPath, actions.GetProperty("recipientAuthority").GetProperty("path").GetString());
        Assert.Equal(LicenseNoticesV1.ProofHeader, actions.GetProperty("recipientAuthority").GetProperty("proofHeader").GetString());

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
    }

    private static void AssertForbiddenOperationalAndContactFields(JsonElement root)
    {
        var sampleShop = new ShopLicenseNoticeItemV1(
            Guid.NewGuid(), Guid.NewGuid(), LicenseNoticesV1.PurposeTrialReminder, LicenseNoticesV1.ThresholdTrialMinus3Days,
            "Active", LicenseNoticesV1.GuidanceTrialEnding, "notice.trial.minus3",
            P0209Fixture.Utc("2028-02-12T04:30:00Z"), P0209Fixture.Utc("2028-02-15T04:30:00Z"), BillingCalendar.Zone, "en-IN", "North Shop");
        using var serializedShop = JsonDocument.Parse(JsonSerializer.Serialize(sampleShop));
        foreach (var forbidden in root.GetProperty("forbiddenOperationalFields").EnumerateArray())
            Assert.False(serializedShop.RootElement.TryGetProperty(forbidden.GetString()!, out _));

        var sampleContact = new BillingContactV1(
            Guid.NewGuid(), Guid.NewGuid(), null, "***@example.test", "en-IN",
            P0209Fixture.Utc("2028-02-01T04:30:00Z"), null, null, null, 1, false);
        using var serializedContact = JsonDocument.Parse(JsonSerializer.Serialize(sampleContact));
        foreach (var forbidden in root.GetProperty("forbiddenContactFields").EnumerateArray())
            Assert.False(serializedContact.RootElement.TryGetProperty(forbidden.GetString()!, out _));

        var sampleActor = new NoticeRecipientActorV1(Guid.NewGuid(), true, false, null, true, "en-IN", "Staff", 1);
        using var serializedActor = JsonDocument.Parse(JsonSerializer.Serialize(sampleActor));
        foreach (var forbidden in root.GetProperty("forbiddenAuthorityWireFields").EnumerateArray())
            Assert.False(serializedActor.RootElement.TryGetProperty(forbidden.GetString()!, out _));
    }

    private static void AssertContractTimeVectors(JsonElement vectors)
    {
        var trialEnd = vectors.GetProperty("trialEnd").GetDateTimeOffset();
        var trialFacts = new SubscriptionAccessFacts(trialEnd.AddDays(-14), trialEnd, TermEvidenceState.Complete, SubscriptionBasis.Trial);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, vectors.GetProperty("trialMinus3Days").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, vectors.GetProperty("trialMinus1Day").GetDateTimeOffset()),
            ],
            NotificationSchedulePolicy.PlanThresholds(trialFacts, trialEnd.AddDays(-14), false, false).Select(i => (i.Threshold, i.DueAt)).ToArray());

        var paidEnd = vectors.GetProperty("paidTermEnd").GetDateTimeOffset();
        var anchor = new BillingAnchor(new DateTime(2028, 1, 31, 10, 0, 0, DateTimeKind.Unspecified), BillingCalendar.Zone, BillingPeriod.Month);
        var paidFacts = new SubscriptionAccessFacts(BillingCalendar.TryBoundary(anchor, 0)!.Value, paidEnd, TermEvidenceState.Complete, SubscriptionBasis.Paid, anchor, 0);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdPaidMinus7Days, vectors.GetProperty("paidMinus7Days").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdPaidMinus1Day, vectors.GetProperty("paidMinus1Day").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdGraceStart, vectors.GetProperty("graceStart").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdGraceMinus1Day, vectors.GetProperty("graceMinus1Day").GetDateTimeOffset()),
                (LicenseNoticesV1.ThresholdGraceExpiry, vectors.GetProperty("graceExpiry").GetDateTimeOffset()),
            ],
            NotificationSchedulePolicy.PlanThresholds(paidFacts, paidFacts.StartsAt, false, false).Select(i => (i.Threshold, i.DueAt)).ToArray());
    }

    private static void AssertRecipientAuthorityResponseValidation()
    {
        var accountId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var affiliationId = Guid.NewGuid();
        var user1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var user2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var now = P0209Fixture.Utc("2028-02-01T04:30:00Z");

        var actorReq = new NoticeRecipientAuthorityRequestV1(1, Guid.NewGuid(), now.AddSeconds(15), accountId, null, null, [user1], null, 10);
        var unsolicitedActor = new NoticeRecipientAuthorityResponseV1(
            1, actorReq.Challenge, accountId, null, null, true, now,
            [new NoticeRecipientActorV1(user2, true, true, "u2@example.test", false, "en-IN", null, null)], [], null);
        Assert.False(unsolicitedActor.IsValid(actorReq));

        var shopReq = new NoticeRecipientAuthorityRequestV1(1, Guid.NewGuid(), now.AddSeconds(15), accountId, affiliationId, shopId, [], user1, 1);
        var staffWithEmail = new NoticeRecipientAuthorityResponseV1(
            1, shopReq.Challenge, accountId, shopId, affiliationId, true, now, [],
            [new NoticeRecipientActorV1(user2, true, true, "staff@example.test", false, "en-IN", "Staff", 1)], null);
        Assert.False(staffWithEmail.IsValid(shopReq));

        var mismatchedWithRoster = new NoticeRecipientAuthorityResponseV1(
            1, shopReq.Challenge, accountId, shopId, affiliationId, false, now, [],
            [new NoticeRecipientActorV1(user2, true, true, null, false, "en-IN", "Staff", 1)], null);
        Assert.False(mismatchedWithRoster.IsValid(shopReq));

        var nonProgressCursor = new NoticeRecipientAuthorityResponseV1(
            1, shopReq.Challenge, accountId, shopId, affiliationId, true, now, [],
            [new NoticeRecipientActorV1(user1, true, true, null, false, "en-IN", "Staff", 1)], user1);
        Assert.False(nonProgressCursor.IsValid(shopReq));

        var oversizedPage = new NoticeRecipientAuthorityResponseV1(
            1, shopReq.Challenge, accountId, shopId, affiliationId, true, now, [],
            [
                new NoticeRecipientActorV1(user2, true, true, null, false, "en-IN", "Staff", 1),
                new NoticeRecipientActorV1(Guid.Parse("00000000-0000-0000-0000-000000000003"), true, true, null, false, "en-IN", "Staff", 1),
            ],
            null);
        Assert.False(oversizedPage.IsValid(shopReq));

        var validPage = new NoticeRecipientAuthorityResponseV1(
            1, shopReq.Challenge, accountId, shopId, affiliationId, true, now, [],
            [new NoticeRecipientActorV1(user2, true, true, null, false, "en-IN", "Staff", 1)], user2);
        Assert.True(validPage.IsValid(shopReq));
    }

    private async Task ApplyFreshMigrationAndProductionGrantsAsync(string dbName, NpgsqlConnectionStringBuilder migrator)
    {
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

        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            REVOKE ALL ON SCHEMA public FROM licensing_runtime;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.due_notification_planning(timestamptz, uuid, integer) TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.account_notification_billing_members(uuid, uuid, integer) TO licensing_runtime;
            """);
    }

    private static async Task AssertForcedRlsAndUniqueIndexesAsync(NpgsqlConnectionStringBuilder migrator)
    {
        await using var conn = new NpgsqlConnection(migrator.ConnectionString);
        await conn.OpenAsync();
        await using var rls = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relname IN ('notification_jobs', 'billing_notice_contacts', 'billing_notice_contact_events', 'billing_notice_contact_operations')
              AND c.relrowsecurity
              AND c.relforcerowsecurity
            """, conn);
        Assert.Equal(4L, (long)(await rls.ExecuteScalarAsync())!);

        await using var idx = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'notification_jobs'
              AND indexname IN ('ux_notification_jobs_semantic_key', 'ux_notification_jobs_email_destination')
            """, conn);
        Assert.Equal(2L, (long)(await idx.ExecuteScalarAsync())!);
    }

    private static async Task AssertRuntimeRlsAndImmutabilityAsync(
        NpgsqlConnectionStringBuilder runtime,
        Guid accountId,
        Guid ownerId)
    {
        var jobId = Guid.NewGuid();
        var dueAt = P0209Fixture.Utc("2028-02-22T04:30:00Z");
        await using var rt = new NpgsqlConnection(runtime.ConnectionString);
        await rt.OpenAsync();

        var unscopedInsert = await Assert.ThrowsAsync<PostgresException>(() => InsertRawJobAsync(rt, null, jobId, accountId, ownerId, dueAt));
        Assert.Equal("42501", unscopedInsert.SqlState);

        await using (var tx = await rt.BeginTransactionAsync())
        {
            await SetScopeGucAsync(rt, tx, "app.billing_account_id", accountId.ToString("D"));
            Assert.Equal(1, await InsertRawJobAsync(rt, tx, jobId, accountId, ownerId, dueAt));
            await tx.CommitAsync();
        }

        await using (var unscopedRead = new NpgsqlCommand("SELECT count(*) FROM public.notification_jobs", rt))
            Assert.Equal(0L, (long)(await unscopedRead.ExecuteScalarAsync())!);

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var truncate = new NpgsqlCommand("TRUNCATE public.notification_jobs", rt);
            await truncate.ExecuteNonQueryAsync();
        });

        await using (var tx = await rt.BeginTransactionAsync())
        {
            await SetScopeGucAsync(rt, tx, "app.billing_account_id", accountId.ToString("D"));
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var upd = new NpgsqlCommand("UPDATE public.notification_jobs SET status = 'Scheduled' WHERE id = $1", rt, tx);
                upd.Parameters.AddWithValue(jobId);
                await upd.ExecuteNonQueryAsync();
            });
            await tx.RollbackAsync();
        }
    }

    private static async Task AssertDiscoveryIsolationAcrossScopesAsync(
        NpgsqlConnectionStringBuilder bootstrap,
        NpgsqlConnectionStringBuilder runtime,
        Guid accountId)
    {
        // Seed one active paid term on accountId so discovery has an eligible account to return in worker scope.
        var now = P0209Fixture.Utc("2028-02-01T04:30:00Z");
        var termId = Guid.NewGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(bootstrap, $"""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ('{termId:D}', '{accountId:D}', 'Independent', 1, '2028-01-31T04:30:00Z', '2028-02-29T04:30:00Z');
            INSERT INTO public.subscription_term_facts (
                account_id, term_id, basis, billing_period, anchor_local, time_zone, period_ordinal,
                member_allowance, price_paise, currency, plan_version_id, source_reference,
                development_fixture, calendar_version, access_policy_version, recorded_at)
            VALUES (
                '{accountId:D}', '{termId:D}', 'Paid', 'Month', '2028-01-31 10:00:00', 'Asia/Kolkata', 0,
                5, 12000, 'INR', NULL, 'payment-fulfilment/1', true, '{BillingCalendar.Version}', '{GracePolicy.Version}',
                '2028-01-31T04:30:00Z');
            """);

        await using var rt = new NpgsqlConnection(runtime.ConnectionString);
        await rt.OpenAsync();

        // Worker scope (no session GUCs) pages eligible accounts.
        Assert.Equal([accountId], await DiscoverInScopeAsync(rt, now, gucName: null, gucValue: null));

        // Actor-only, account, shop, and provider-session scopes MUST return 0 identifiers!
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.billing_actor_id", Guid.NewGuid().ToString("D")));
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.billing_account_id", accountId.ToString("D")));
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.status_shop_id", Guid.NewGuid().ToString("D")));
        Assert.Empty(await DiscoverInScopeAsync(rt, now, "app.provider_session_id", Guid.NewGuid().ToString("D")));
    }

    private static async Task AssertRollbackAndReplayAsync(NpgsqlConnectionStringBuilder migrator)
    {
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(migrator.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new LicensingDbContext(options);
        var migratorService = context.Database.GetService<IMigrator>();
        await migratorService.MigrateAsync("20261127074702_Baseline");
        Assert.Equal(DBNull.Value, await LicensingPostgreSqlFixture.ReadScalarAsync(migrator, "SELECT to_regclass('public.notification_jobs')::text"));
        Assert.Equal(DBNull.Value, await LicensingPostgreSqlFixture.ReadScalarAsync(migrator, "SELECT to_regclass('public.billing_notice_contact_operations')::text"));

        await migratorService.MigrateAsync();
        Assert.Equal("notification_jobs", await LicensingPostgreSqlFixture.ReadScalarAsync(migrator, "SELECT to_regclass('public.notification_jobs')::text"));
        Assert.Equal("billing_notice_contact_operations", await LicensingPostgreSqlFixture.ReadScalarAsync(migrator, "SELECT to_regclass('public.billing_notice_contact_operations')::text"));
    }

    private static async Task<Guid[]> DiscoverInScopeAsync(
        NpgsqlConnection rt,
        DateTimeOffset now,
        string? gucName,
        string? gucValue)
    {
        await using var tx = await rt.BeginTransactionAsync();
        if (gucName is not null && gucValue is not null)
            await SetScopeGucAsync(rt, tx, gucName, gucValue);

        await using var cmd = new NpgsqlCommand("SELECT account_id FROM public.due_notification_planning($1, NULL, 20)", rt, tx);
        cmd.Parameters.AddWithValue(now);
        var list = new List<Guid>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                list.Add(reader.GetGuid(0));
        }

        await tx.RollbackAsync();
        return [.. list];
    }

    private static async Task SetScopeGucAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string name, string value)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config($1, $2, true)", conn, tx);
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertRawJobAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        Guid jobId,
        Guid accountId,
        Guid ownerId,
        DateTimeOffset dueAt)
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
            """, conn, tx);
        cmd.Parameters.AddWithValue(jobId);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue($"user:{ownerId:D}");
        cmd.Parameters.AddWithValue(ownerId);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(dueAt);
        cmd.Parameters.AddWithValue(dueAt.AddDays(7));
        return await cmd.ExecuteNonQueryAsync();
    }

    private static void AssertStringSet(string[] expected, JsonElement actual) =>
        Assert.Equal(expected, actual.EnumerateArray().Select(item => item.GetString()!).ToArray());
}
