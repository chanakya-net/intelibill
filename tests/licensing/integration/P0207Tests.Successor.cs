using System.Net;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0207Tests
{
    [Fact]
    public async Task SuccessorPublication_PreservesAcceptedFactsAndSerializesRaces()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, seed) = await fixture.ManagerAsync();
        using var session = manager;
        using var other = await fixture.SignInOperatorAsync(seed);
        var trial = await PublishTrialAsync(manager);
        var plan = Guid.NewGuid();
        var original = await SavePlanAsync(manager, plan, PlanFacts(0), HttpStatusCode.Created);
        var originalId = original.GetProperty("versionId").GetGuid();
        var firstPath = PlanPath(plan, originalId);
        var first = await PrepareAsync(manager, firstPath, Guid.NewGuid(), 1, trial);
        using (var response = await ConfirmAsync(manager, firstPath, first))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await SeedCoverageAsync(fixture.Superuser);
        var before = await AgreementFactsAsync(fixture, originalId);
        var newerTrial = await PublishTrialSuccessorAsync(manager, trial);
        var successor = PlanFacts(0, 241000);
        successor["sourceVersionId"] = originalId;
        successor["sourceExpectedVersion"] = 2;
        successor["membersPerShop"] = 7;
        var draft = await CreateSuccessorConcurrentlyAsync(fixture, manager, other, plan, successor);
        Assert.Equal(2, draft.GetProperty("versionNumber").GetInt32());
        var latestId = draft.GetProperty("versionId").GetGuid();
        var path = PlanPath(plan, latestId);
        var latest = await PrepareAsync(manager, path, Guid.NewGuid(), 1, newerTrial);
        Assert.Equal(newerTrial, latest.GetProperty("snapshot").GetProperty("plan").GetProperty("trialPolicy").GetProperty("versionId").GetGuid());
        Assert.Equal(3, await fixture.ScalarAsync($"SELECT status FROM trial_policy_versions WHERE id = '{trial}'"));
        await ConfirmConcurrentlyAsync(fixture, manager, other, path, latest);
        Assert.Equal(3, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{originalId}'"));
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{latestId}'"));
        Assert.Equal(1L, await fixture.ScalarAsync($"SELECT count(*) FROM catalogue_publications WHERE version_id = '{latestId}'"));
        Assert.Equal(before, await AgreementFactsAsync(fixture, originalId));

        using var replay = await ConfirmAsync(manager, firstPath, first);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first.GetProperty("snapshot").GetRawText(),
            (await P0206Fixture.BodyAsync(replay)).GetProperty("snapshot").GetRawText());
        var refused = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"""
            UPDATE plan_versions SET monthly_price_paise = 100, revision = revision + 1
            WHERE id = '{originalId}'
            """));
        Assert.Equal("55000", refused.SqlState);
    }

    private static async Task<Guid> PublishTrialSuccessorAsync(P0110CookieBrowser manager, Guid trial)
    {
        using var draft = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial, new
        {
            expectedVersion = 0, sourceVersionId = trial, sourceExpectedVersion = 2,
            activeMemberCap = 8, maxConcurrentTrialShops = 4, trialTermsVersion = "trial-local-1",
        }, await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var id = (await P0206Fixture.BodyAsync(draft)).GetProperty("draft").GetProperty("versionId").GetGuid();
        var path = $"/api/admin/trial-policy/versions/{id}";
        var preview = await PrepareAsync(manager, path, Guid.NewGuid(), 1);
        using var published = await ConfirmAsync(manager, path, preview);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return id;
    }

    private static async Task ConfirmConcurrentlyAsync(P0206Fixture fixture, P0110CookieBrowser first,
        P0110CookieBrowser second, string path, JsonElement prepared)
    {
        await using var connection = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await connection.OpenAsync();
        await using var gate = await connection.BeginTransactionAsync();
        var key = prepared.GetProperty("operationKey").GetGuid();
        var actor = (Guid)(await fixture.ScalarAsync(
            $"SELECT id FROM admin_identities WHERE lower(email) = lower('{P0114Tests.Invitee}')"))!;
        await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, gate);
        hold.Parameters.AddWithValue($"catalogue:operation:{actor:D}:{key:D}");
        await hold.ExecuteNonQueryAsync();
        var confirmations = new[] { ConfirmAsync(first, path, prepared), ConfirmAsync(second, path, prepared) };
        await WaitForAdvisoryWaitersAsync(fixture, 2);
        await gate.CommitAsync();
        foreach (var pending in confirmations)
        {
            using var response = await pending;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task WaitForAdvisoryWaitersAsync(P0206Fixture fixture, int expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
        while (!deadline.IsCancellationRequested)
        {
            var waiting = await fixture.ScalarAsync("""
                SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()
                    AND usename = 'licensing_runtime' AND wait_event = 'advisory'
                    AND query LIKE '%pg_advisory_xact_lock%'
                """);
            if ((long)waiting! >= expected) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Expected {expected} catalogue writers waiting on an advisory lock.");
    }

    private static string PlanPath(Guid plan, Guid version) => $"/api/admin/plans/{plan}/versions/{version}";

    internal static async Task SeedCoverageAsync(NpgsqlConnectionStringBuilder settings)
    {
        var actor = Guid.NewGuid();
        var account = Guid.NewGuid();
        var request = Guid.NewGuid();
        var shop = Guid.NewGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(settings, $"""
            INSERT INTO billing_accounts(id, name, created_by_merchant_user_id, creation_key, creation_request_hash, created_at)
                VALUES ('{account}', 'Fixture coverage', '{actor}', gen_random_uuid(), decode(repeat('00', 32), 'hex'), now());
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
                VALUES (gen_random_uuid(), '{account}', '{actor}', 1, 1, 1, now());
            """);
        var now = P0202Fixture.WholeSecondNow();
        var term = await P0205Tests.SeedTermAsync(settings, account, now.AddDays(-7), now.AddDays(23), grouped: true);
        await LicensingPostgreSqlFixture.ExecuteAsync(settings, $"""
            INSERT INTO shop_affiliation_requests(request_id, account_id, account_name, shop_id, requester_id,
                requester_revision, operation_key, term_id, created_at, expires_at)
                VALUES ('{request}', '{account}', 'Fixture coverage', '{shop}', '{actor}', 1, gen_random_uuid(),
                    '{term}', now() - interval '1 day', now() + interval '1 day');
            INSERT INTO shop_affiliations(request_id, account_id, shop_id, term_id, event_id, actor_id, membership_id,
                authority_version, shop_name, owner_name, accepted_at, applied_at, coverage_ends_at)
                SELECT '{request}', '{account}', '{shop}', term_id, gen_random_uuid(), '{actor}', gen_random_uuid(),
                    1, 'Fixture store', 'Fixture owner', now(), now(), ends_at
                    FROM subscription_coverage_terms WHERE term_id = '{term}';
            """);
    }

    private static Task<object?> AgreementFactsAsync(P0206Fixture fixture, Guid originalId) =>
        fixture.ScalarAsync($"""
            SELECT json_build_array(
                (SELECT snapshot FROM catalogue_preparations p JOIN catalogue_publications v USING (admin_id, operation_key)
                    WHERE v.version_id = '{originalId}'),
                (SELECT json_agg(t) FROM subscription_coverage_terms t),
                (SELECT json_agg(a) FROM shop_affiliations a))::text
            """);
}
