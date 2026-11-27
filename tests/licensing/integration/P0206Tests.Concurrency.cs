using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0206Tests
{
    [Fact]
    public async Task StaleExpectedVersion_IsRefusedAndLeavesDraftUnchanged()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        var draftId = (await SaveAsync(manager, planId, MultiShop(0), HttpStatusCode.Created)).GetProperty("versionId").GetGuid();
        await SaveAsync(manager, planId, MultiShop(1, ("membersPerShop", 6), (DraftId, draftId)));

        foreach (var (version, status, code) in new[]
                 {
                     (1, HttpStatusCode.Conflict, "Catalogue.DraftVersionConflict"),
                     (3, HttpStatusCode.Conflict, "Catalogue.DraftVersionConflict"),
                     (0, HttpStatusCode.Conflict, "Catalogue.DraftVersionConflict"),
                 })
        {
            using var stale = await P0206Fixture.PutPlanAsync(manager, planId,
                MultiShop(version, ("membersPerShop", 99), (DraftId, version == 0 ? null : draftId)));
            Assert.Equal(status, stale.StatusCode);
            Assert.Equal(code, await P0206Fixture.CodeAsync(stale));
        }
        using var unknown = await P0206Fixture.PutPlanAsync(manager, Guid.NewGuid(), MultiShop(1));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("Catalogue.DraftNotFound", await P0206Fixture.CodeAsync(unknown));

        Assert.Equal(6, await fixture.ScalarAsync($"SELECT members_per_shop FROM plan_versions WHERE plan_id = '{planId}'"));
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT revision FROM plan_versions WHERE plan_id = '{planId}'"));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
    }

    [Fact]
    public async Task ConcurrentSaves_CommitExactlyOneRevision()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (first, seed) = await fixture.ManagerAsync();
        using var _first = first;
        using var second = await fixture.SignInOperatorAsync(seed);
        var planId = Guid.NewGuid();

        var creates = await Task.WhenAll(
            StatusAsync(P0206Fixture.PutPlanAsync(first, planId, MultiShop(0))),
            StatusAsync(P0206Fixture.PutPlanAsync(second, planId, MultiShop(0, ("name", "Other")))));
        Assert.Single(creates, status => status == HttpStatusCode.Created);
        Assert.Single(creates, status => status == HttpStatusCode.Conflict);
        var draftId = (Guid)(await fixture.ScalarAsync($"SELECT id FROM plan_versions WHERE plan_id = '{planId}'"))!;

        // Hold the draft row so one save waits for the row and its competitor queues behind the same plan fence.
        var proofs = (await P0206Fixture.ProofAsync(first), await P0206Fixture.ProofAsync(second));
        await using var gate = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await gate.OpenAsync();
        var saves = new List<Task<HttpStatusCode>>();
        await using (var hold = await gate.BeginTransactionAsync())
        {
            await using (var lockRow = new NpgsqlCommand(
                             $"SELECT 1 FROM plan_versions WHERE plan_id = '{planId}' FOR UPDATE", gate, hold))
                await lockRow.ExecuteNonQueryAsync();
            saves.Add(StatusAsync(first.SendAsync(HttpMethod.Put, $"{Plans}/{planId}/draft",
                MultiShop(1, ("membersPerShop", 7), (DraftId, draftId)), proofs.Item1)));
            saves.Add(StatusAsync(second.SendAsync(HttpMethod.Put, $"{Plans}/{planId}/draft",
                MultiShop(1, ("membersPerShop", 8), (DraftId, draftId)), proofs.Item2)));
            await WaitForDraftLockAsync(fixture, 2);
            await hold.RollbackAsync();
        }
        var outcomes = await Task.WhenAll(saves);
        Assert.Single(outcomes, status => status == HttpStatusCode.OK);
        Assert.Single(outcomes, status => status == HttpStatusCode.Conflict);
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT revision FROM plan_versions WHERE plan_id = '{planId}'"));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM plan_versions"));
    }

    private static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> pending)
    {
        using var response = await pending;
        return response.StatusCode;
    }

    private static async Task WaitForDraftLockAsync(P0206Fixture fixture, int expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!deadline.IsCancellationRequested)
        {
            var waiting = await fixture.ScalarAsync("""
                SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()
                  AND usename = 'licensing_runtime' AND wait_event_type = 'Lock'
                  AND (query LIKE '%plan_versions%FOR UPDATE%' OR wait_event = 'advisory')
                """);
            if ((long)waiting! >= expected) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Expected {expected} saves waiting on the draft row or plan advisory lock.");
    }
}
