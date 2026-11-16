using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// A successor draft restarts its revision at 1, so an editor still open on the published predecessor holds a
/// request whose revision matches the successor. Ordinary saves are bound to the exact draft version and fail closed.
/// </summary>
public sealed partial class P0207Tests
{
    [Fact]
    public async Task StalePredecessorPlanSave_IsRefusedAndLeavesTheSuccessorUntouched()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var trial = await PublishTrialAsync(manager);
        var plan = Guid.NewGuid();
        var predecessorId = (await SavePlanAsync(manager, plan, PlanFacts(0), HttpStatusCode.Created))
            .GetProperty("versionId").GetGuid();
        var legacyEditor = PlanFacts(1, 999900);
        var staleEditor = PlanFacts(1, 999900);
        staleEditor["expectedDraftVersionId"] = predecessorId;
        var path = PlanPath(plan, predecessorId);
        using (var published = await ConfirmAsync(manager, path, await PrepareAsync(manager, path, Guid.NewGuid(), 1, trial)))
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var successorFacts = PlanFacts(0, 241000);
        successorFacts["sourceVersionId"] = predecessorId;
        successorFacts["sourceExpectedVersion"] = 2;
        successorFacts["membersPerShop"] = 7;
        var successor = await SavePlanAsync(manager, plan, successorFacts, HttpStatusCode.Created);
        var successorId = successor.GetProperty("versionId").GetGuid();
        Assert.Equal(1, successor.GetProperty("version").GetInt32());
        var before = await PlanVersionFactsAsync(fixture, successorId);
        var changes = await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes");

        foreach (var stale in new[] { legacyEditor, staleEditor })
        {
            using var refused = await P0206Fixture.PutPlanAsync(manager, plan, stale);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("Catalogue.DraftVersionConflict", await P0206Fixture.CodeAsync(refused));
        }
        Assert.Equal(before, await PlanVersionFactsAsync(fixture, successorId));
        Assert.Equal(changes, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));

        var current = PlanFacts(1, 250000);
        current["membersPerShop"] = 7;
        current["expectedDraftVersionId"] = successorId;
        var saved = await SavePlanAsync(manager, plan, current, HttpStatusCode.OK);
        Assert.Equal(successorId, saved.GetProperty("versionId").GetGuid());
        Assert.Equal(2, saved.GetProperty("version").GetInt32());
        Assert.Equal(250000L, saved.GetProperty("annualPricePaise").GetInt64());
        Assert.Equal((long)changes! + 1, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
    }

    [Fact]
    public async Task StalePredecessorTrialSave_IsRefusedAndLeavesTheSuccessorUntouched()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var predecessorId = await CreateTrialDraftAsync(manager);
        var legacyEditor = new { expectedVersion = 1, activeMemberCap = 9, maxConcurrentTrialShops = 9,
            trialTermsVersion = "trial-local-1" };
        var staleEditor = new { expectedVersion = 1, expectedDraftVersionId = predecessorId, activeMemberCap = 9,
            maxConcurrentTrialShops = 9, trialTermsVersion = "trial-local-1" };
        var path = $"/api/admin/trial-policy/versions/{predecessorId}";
        using (var published = await ConfirmAsync(manager, path, await PrepareAsync(manager, path, Guid.NewGuid(), 1)))
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var successorId = await CreateTrialSuccessorAsync(manager, predecessorId);
        var before = await TrialVersionFactsAsync(fixture, successorId);
        var changes = await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes");

        foreach (var stale in new object[] { legacyEditor, staleEditor })
        {
            using var refused = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial, stale,
                await P0206Fixture.ProofAsync(manager));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("Catalogue.DraftVersionConflict", await P0206Fixture.CodeAsync(refused));
        }
        Assert.Equal(before, await TrialVersionFactsAsync(fixture, successorId));
        Assert.Equal(changes, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));

        using var saved = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial, new
        {
            expectedVersion = 1, expectedDraftVersionId = successorId, activeMemberCap = 6,
            maxConcurrentTrialShops = 3, trialTermsVersion = "trial-local-1",
        }, await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var draft = (await P0206Fixture.BodyAsync(saved)).GetProperty("draft");
        Assert.Equal(successorId, draft.GetProperty("versionId").GetGuid());
        Assert.Equal(2, draft.GetProperty("version").GetInt32());
        Assert.Equal(6, draft.GetProperty("activeMemberCap").GetInt32());
        Assert.Equal((long)changes! + 1, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
    }

    [Fact]
    public async Task DraftIdentityOnACreateRequest_IsRefusedWithoutWriting()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var create = PlanFacts(0);
        create["expectedDraftVersionId"] = Guid.NewGuid();
        using var plan = await P0206Fixture.PutPlanAsync(manager, Guid.NewGuid(), create);
        Assert.Equal(HttpStatusCode.BadRequest, plan.StatusCode);
        Assert.Equal("Catalogue.DraftInvalid", await P0206Fixture.CodeAsync(plan));
        using var trial = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial,
            new { expectedVersion = 0, expectedDraftVersionId = Guid.NewGuid(), activeMemberCap = 5 },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.BadRequest, trial.StatusCode);
        Assert.Equal("Catalogue.DraftInvalid", await P0206Fixture.CodeAsync(trial));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
    }

    private static async Task<Guid> CreateTrialSuccessorAsync(P0110CookieBrowser manager, Guid source)
    {
        using var draft = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial, new
        {
            expectedVersion = 0, sourceVersionId = source, sourceExpectedVersion = 2,
            activeMemberCap = 8, maxConcurrentTrialShops = 4, trialTermsVersion = "trial-local-1",
        }, await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var body = (await P0206Fixture.BodyAsync(draft)).GetProperty("draft");
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        return body.GetProperty("versionId").GetGuid();
    }

    private static Task<object?> PlanVersionFactsAsync(P0206Fixture fixture, Guid id) =>
        fixture.ScalarAsync($"SELECT row_to_json(v)::text FROM plan_versions v WHERE id = '{id}'");

    private static Task<object?> TrialVersionFactsAsync(P0206Fixture fixture, Guid id) =>
        fixture.ScalarAsync($"SELECT row_to_json(v)::text FROM trial_policy_versions v WHERE id = '{id}'");
}
