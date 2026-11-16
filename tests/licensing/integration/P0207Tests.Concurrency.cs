using System.Net;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0207Tests
{
    [Fact]
    public async Task ConcurrentDraftEditInvalidatesPreparedPublicationWithoutAnOffer()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, seed) = await fixture.ManagerAsync();
        using var publisher = manager;
        using var editor = await fixture.SignInOperatorAsync(seed);
        var trial = await PublishTrialAsync(manager);
        var plan = Guid.NewGuid();
        var draft = await SavePlanAsync(manager, plan, PlanFacts(0), HttpStatusCode.Created);
        var id = draft.GetProperty("versionId").GetGuid();
        var path = PlanPath(plan, id);
        var prepared = await PrepareAsync(manager, path, Guid.NewGuid(), 1, trial);

        await using var connection = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await connection.OpenAsync();
        await using var gate = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT id FROM plan_versions WHERE id = $1 FOR UPDATE", connection, gate);
        hold.Parameters.AddWithValue(id);
        Assert.Equal(id, await hold.ExecuteScalarAsync());
        var edit = PlanFacts(1, 999900);
        edit["expectedDraftVersionId"] = id;
        var saving = P0206Fixture.PutPlanAsync(editor, plan, edit);
        await WaitForSaveRowLockAsync(fixture);
        var confirming = ConfirmAsync(manager, path, prepared);
        await WaitForAdvisoryWaitersAsync(fixture, 1);
        await gate.CommitAsync();

        using var saved = await saving;
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var conflicted = await confirming;
        Assert.Equal(HttpStatusCode.Conflict, conflicted.StatusCode);
        Assert.Equal(0L, await fixture.ScalarAsync($"SELECT count(*) FROM catalogue_publications WHERE version_id = '{id}'"));
        Assert.Equal(0L, await fixture.ScalarAsync($"SELECT count(*) FROM published_catalogue_offers WHERE version_id = '{id}'"));
        Assert.Equal(999900L, await fixture.ScalarAsync($"SELECT annual_price_paise FROM plan_versions WHERE id = '{id}' AND status = 1"));
    }

    private static async Task WaitForSaveRowLockAsync(P0206Fixture fixture)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
        while (!deadline.IsCancellationRequested)
        {
            var waiting = await fixture.ScalarAsync("""
                SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()
                    AND usename = 'licensing_runtime' AND wait_event_type = 'Lock'
                    AND query LIKE '%plan_versions%FOR UPDATE%'
                """);
            if ((long)waiting! == 1) return;
            await Task.Delay(10);
        }
        Assert.Fail("The draft save did not wait on its PostgreSQL row lock.");
    }

    private static async Task<JsonElement> CreateSuccessorConcurrentlyAsync(P0206Fixture fixture, P0110CookieBrowser first,
        P0110CookieBrowser second, Guid plan, object successor)
    {
        await using var connection = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await connection.OpenAsync();
        await using var gate = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, gate);
        hold.Parameters.AddWithValue($"catalogue:plan:{plan:D}");
        await hold.ExecuteNonQueryAsync();
        var saves = new[]
        {
            P0206Fixture.PutPlanAsync(first, plan, successor), P0206Fixture.PutPlanAsync(second, plan, successor),
        };
        await WaitForAdvisoryWaitersAsync(fixture, 2);
        await gate.CommitAsync();
        using var a = await saves[0];
        using var b = await saves[1];
        var outcomes = new[] { a, b };
        Assert.Single(outcomes, answer => answer.StatusCode == HttpStatusCode.Conflict);
        var created = Assert.Single(outcomes, answer => answer.StatusCode == HttpStatusCode.Created);
        return (await P0206Fixture.BodyAsync(created)).GetProperty("draft");
    }
}
