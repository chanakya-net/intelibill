using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0207Tests
{
    [Fact]
    public async Task FixturePublication_IsRefusedByDisabledLiveAndNonLocalHosts()
    {
        var settings = LocalSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var fixture = await P0206Fixture.StartAsync(database, settings);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var draft = await CreateTrialDraftAsync(manager);
        var path = $"/api/admin/trial-policy/versions/{draft}";
        var prepared = await PrepareAsync(manager, path, Guid.NewGuid(), 1);
        var settingsOverride = P0108Tests.SignInMfaSettings();
        foreach (var (name, value) in settings) settingsOverride[name] = value;
        settingsOverride[$"CataloguePublication:ApprovedSnapshots:{draft:D}"] = prepared.GetProperty("snapshotHash").GetString();

        foreach (var (environment, mode) in new[]
                 {
                     ("Testing", "Disabled"), ("Testing", "Live"), ("Production", "LocalFixture"), ("Production", "Live"),
                 })
        {
            settingsOverride["CataloguePublication:Mode"] = mode;
            await using var alternate = new LicensingApiFactory(fixture.Runtime, environment: environment,
                clock: fixture.Clock, settings: settingsOverride);
            using var actor = manager.Fork(P0114Tests.RawClient(alternate));
            using var denied = await actor.SendAsync(HttpMethod.Post, path + "/publish-preview",
                new { operationKey = Guid.NewGuid(), expectedVersion = 1, reason = "Local fixture" },
                await P0206Fixture.ProofAsync(actor));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Catalogue.PublicationNotApproved", await P0206Fixture.CodeAsync(denied));
            using var confirm = await ConfirmAsync(actor, path, prepared);
            Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        }

        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_publications"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM published_catalogue_offers"));
    }

    [Fact]
    public async Task CompletePlanRequiresAllPricesAndConfiguredPolicyVersions()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var trial = await PublishTrialAsync(manager);
        foreach (var (field, value, status, code) in new (string, object?, HttpStatusCode, string)[]
                 {
                     ("annualPricePaise", null, HttpStatusCode.BadRequest, "Catalogue.Incomplete"),
                     ("extraShopAnnualPricePaise", null, HttpStatusCode.BadRequest, "Catalogue.Incomplete"),
                     ("refundPolicyVersion", "unapproved-terms", HttpStatusCode.Forbidden, "Catalogue.PublicationNotApproved"),
                 })
        {
            var facts = PlanFacts(0);
            facts[field] = value;
            var plan = Guid.NewGuid();
            var draft = await SavePlanAsync(manager, plan, facts, HttpStatusCode.Created);
            var path = PlanPath(plan, draft.GetProperty("versionId").GetGuid());
            using var response = await manager.SendAsync(HttpMethod.Post, path + "/publish-preview",
                new { operationKey = Guid.NewGuid(), expectedVersion = 1, trialPolicyVersionId = trial, reason = "Local approval" },
                await P0206Fixture.ProofAsync(manager));
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await P0206Fixture.CodeAsync(response));
        }
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_preparations WHERE plan_id IS NOT NULL"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM published_catalogue_offers"));
    }

    [Fact]
    public async Task OperationRecoveryIsActorBoundAndRechecksLiveAuthority()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, seed) = await fixture.ManagerAsync();
        using var session = manager;
        var (other, _) = await fixture.ManagerAsync("second-catalogue-manager@example.test");
        using var otherSession = other;
        var draft = await CreateTrialDraftAsync(manager);
        var path = $"/api/admin/trial-policy/versions/{draft}";
        var key = Guid.NewGuid();
        var prepared = await PrepareAsync(manager, path, key, 1);
        var recoveryPath = "/api/admin/catalogue/operations/" + key;
        using var refused = await other.SendAsync(HttpMethod.Get, recoveryPath);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        using var different = await manager.SendAsync(HttpMethod.Post, path + "/publish-preview",
            new { operationKey = key, expectedVersion = 1, reason = "Different commercial reason" },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);

        using var changedHash = await manager.SendAsync(HttpMethod.Post, path + "/publish",
            new { operationKey = key, expectedVersion = 1, snapshotHash = new string('0', 64), confirmed = true },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Conflict, changedHash.StatusCode);
        using var confirmation = await ConfirmAsync(manager, path, prepared);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        using var expiredMfa = await manager.SendAsync(HttpMethod.Get, recoveryPath);
        Assert.Equal("Catalogue.RecentMfaRequired", await P0206Fixture.CodeAsync(expiredMfa));
        using var fresh = await fixture.SignInOperatorAsync(seed);
        using var recovered = await fresh.SendAsync(HttpMethod.Get, recoveryPath);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal((await P0206Fixture.BodyAsync(confirmation)).GetRawText(),
            (await P0206Fixture.BodyAsync(recovered)).GetRawText());
        await fixture.GrantAsync(P0114Tests.Invitee);
        using var revoked = await fresh.SendAsync(HttpMethod.Get, recoveryPath);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task MigrationKeepsPreExistingDraftFactsUnapproved()
    {
        var name = "p0207_upgrade_" + Guid.NewGuid().ToString("N");
        using var fixture = new P0201Fixture(database, name);
        var superuser = database.Bootstrap(name);
        await database.CreateEmptyLicensingDatabaseAsync(name);
        await using var context = fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261116120000_AddBillingOwnershipTransfers");
        await LicensingPostgreSqlFixture.ExecuteAsync(superuser, """
            INSERT INTO trial_policy_versions(id, version_number, status, revision, duration_days, active_member_cap,
                max_concurrent_trial_shops, trial_terms_version, created_by_admin_id, created_at, updated_by_admin_id, updated_at)
            VALUES (gen_random_uuid(), 1, 1, 2, 14, 5, 2, 'trial-local-1', gen_random_uuid(), now(), gen_random_uuid(), now());
            """);
        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(true, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, """
            SELECT status = 1 AND revision = 2 AND source = 0 AND source_version_id IS NULL AND source_expected_version IS NULL
                AND active_member_cap = 5 AND max_concurrent_trial_shops = 2 AND trial_terms_version = 'trial-local-1'
            FROM trial_policy_versions
            """));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, "SELECT count(*) FROM catalogue_publications"));
    }

    private static async Task<Guid> CreateTrialDraftAsync(P0110CookieBrowser manager, int maxConcurrentTrialShops = 2)
    {
        using var draft = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial,
            new { expectedVersion = 0, activeMemberCap = 5, maxConcurrentTrialShops, trialTermsVersion = "trial-local-1" },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        return (await P0206Fixture.BodyAsync(draft)).GetProperty("draft").GetProperty("versionId").GetGuid();
    }
}
