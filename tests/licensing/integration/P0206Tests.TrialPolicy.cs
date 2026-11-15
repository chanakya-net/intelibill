using System.Net;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0206Tests
{
    private const string Trial = P0206Fixture.Trial;
    private static readonly string[] TrialGaps = ["maxConcurrentTrialShops", "trialTermsVersion"];

    [Fact]
    public async Task TrialPolicyDraft_RequiresExplicitCaps()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        using (var empty = await manager.SendAsync(HttpMethod.Get, Trial))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await P0206Fixture.BodyAsync(empty)).GetProperty("draft").ValueKind);
        }

        var partial = await SaveTrialAsync(manager, new { expectedVersion = 0, activeMemberCap = 5 }, HttpStatusCode.Created);
        Assert.Equal(TrialGaps, Missing(partial));
        Assert.False(partial.GetProperty("complete").GetBoolean());
        Assert.Equal(14, partial.GetProperty("durationDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, partial.GetProperty("maxConcurrentTrialShops").ValueKind);

        foreach (var (body, code) in new (object Body, string Code)[]
                 {
                     (new { expectedVersion = 1, activeMemberCap = 0 }, "Catalogue.CapacityInvalid"),
                     (new { expectedVersion = 1, maxConcurrentTrialShops = -1 }, "Catalogue.CapacityInvalid"),
                     (new { expectedVersion = 1, activeMemberCap = 100_001 }, "Catalogue.CapacityInvalid"),
                     (new { expectedVersion = 1, durationDays = 30 }, "Catalogue.DraftInvalid"),
                     (new { expectedVersion = 1, trialTermsVersion = "trial terms" }, "Catalogue.PolicyVersionInvalid"),
                 })
        {
            using var refused = await manager.SendAsync(HttpMethod.Put, Trial, body, await P0206Fixture.ProofAsync(manager));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(code, await P0206Fixture.CodeAsync(refused));
        }

        var complete = await SaveTrialAsync(manager, new
        {
            expectedVersion = 1, activeMemberCap = 5, maxConcurrentTrialShops = 2, trialTermsVersion = "trial-2026.11",
        });
        Assert.True(complete.GetProperty("complete").GetBoolean());
        Assert.Equal("draft", complete.GetProperty("status").GetString());
        Assert.Equal(2, complete.GetProperty("version").GetInt32());
        using var stale = await manager.SendAsync(HttpMethod.Put, Trial, new { expectedVersion = 1, activeMemberCap = 9 },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(1L, await fixture.ScalarAsync("""
            SELECT count(*) FROM trial_policy_versions WHERE status = 1 AND duration_days = 14 AND active_member_cap = 5
              AND max_concurrent_trial_shops = 2 AND revision = 2
            """));
    }

    [Fact]
    public async Task ConcurrentTrialPolicyCreates_KeepOneDraft()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (first, seed) = await fixture.ManagerAsync();
        using var _first = first;
        using var second = await fixture.SignInOperatorAsync(seed);
        var proofs = (await P0206Fixture.ProofAsync(first), await P0206Fixture.ProofAsync(second));
        var outcomes = await Task.WhenAll(
            StatusAsync(first.SendAsync(HttpMethod.Put, Trial, new { expectedVersion = 0, activeMemberCap = 3 }, proofs.Item1)),
            StatusAsync(second.SendAsync(HttpMethod.Put, Trial, new { expectedVersion = 0, activeMemberCap = 4 }, proofs.Item2)));
        Assert.Single(outcomes, status => status == HttpStatusCode.Created);
        Assert.Single(outcomes, status => status == HttpStatusCode.Conflict);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM trial_policy_versions"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes WHERE target_kind = 2"));
    }

    [Fact]
    public async Task TrialPolicyDraft_ReadsCommittedCapsWhileTheDraftRowIsLocked()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        await SaveTrialAsync(manager, new { expectedVersion = 0, activeMemberCap = 5 }, HttpStatusCode.Created);

        await using var gate = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await gate.OpenAsync();
        await using var hold = await gate.BeginTransactionAsync();
        await using var lockRow = new NpgsqlCommand("SELECT active_member_cap FROM trial_policy_versions FOR UPDATE", gate, hold);
        Assert.Equal(5, await lockRow.ExecuteScalarAsync());

        using var response = await manager.SendAsync(HttpMethod.Get, Trial);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await P0206Fixture.BodyAsync(response)).GetProperty("draft");
        Assert.Equal(5, draft.GetProperty("activeMemberCap").GetInt32());
    }

    private static async Task<JsonElement> SaveTrialAsync(P0110CookieBrowser manager, object body,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await manager.SendAsync(HttpMethod.Put, Trial, body, await P0206Fixture.ProofAsync(manager));
        Assert.Equal(expected, response.StatusCode);
        return (await P0206Fixture.BodyAsync(response)).GetProperty("draft");
    }
}
