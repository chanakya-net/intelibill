using System.Net;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0207Tests
{
    private const string CustomerPlans = "/internal/licensing/v1/customer/plans";

    [Fact]
    public async Task MerchantCatalogue_ReturnsOnlyPublishedPublicTerms()
    {
        using var signing = new P0201Fixture(database, "catalogue_signing_fixture");
        var settings = LocalSettings();
        foreach (var (key, value) in signing.LicensingSettings()) settings[key] = value;
        await using var fixture = await P0206Fixture.StartAsync(database, settings);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        using var client = fixture.Api.CreateClient();
        signing.Clock.Now = fixture.Clock.GetUtcNow();
        var actor = Guid.NewGuid();
        var trial = await PublishTrialAsync(manager);
        var plan = Guid.NewGuid();
        var draft = await SavePlanAsync(manager, plan, PlanFacts(0), HttpStatusCode.Created);
        var version = draft.GetProperty("versionId").GetGuid();
        var prepared = await PrepareAsync(manager, PlanPath(plan, version), Guid.NewGuid(), 1, trial);

        P0202Fixture.ActorAssertion Sign() => P0202Fixture.Sign(signing, actor, "billing.catalogue.read", [],
            claims => claims["path"] = CustomerPlans);
        using (var unavailable = await P0202Fixture.SendAsync(client, Sign(), path: CustomerPlans))
        {
            Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
            Assert.Empty((await P0206Fixture.BodyAsync(unavailable)).GetProperty("items").EnumerateArray());
        }
        using (var published = await ConfirmAsync(manager, PlanPath(plan, version), prepared))
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var custom = PlanFacts(0);
        custom["kind"] = "custom";
        custom["name"] = "Private Custom canary";
        custom["reason"] = "Private negotiation canary";
        custom["extraShopsOffered"] = false;
        custom["extraShopMonthlyPricePaise"] = null;
        custom["extraShopAnnualPricePaise"] = null;
        var customPlan = Guid.NewGuid();
        var customDraft = await SavePlanAsync(manager, customPlan, custom, HttpStatusCode.Created);
        var customPath = PlanPath(customPlan, customDraft.GetProperty("versionId").GetGuid());
        var customPreview = await PrepareAsync(manager, customPath, Guid.NewGuid(), 1, trial);
        using (var published = await ConfirmAsync(manager, customPath, customPreview))
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime,
            "SELECT count(*) FROM published_catalogue_offers"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime,
            "SELECT count(*) FROM catalogue_preparations"));
        var assertion = Sign();
        using var listed = await P0202Fixture.SendAsync(client, assertion, path: CustomerPlans);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var offer = Assert.Single((await P0206Fixture.BodyAsync(listed)).GetProperty("items").EnumerateArray());
        Assert.Equal(version, offer.GetProperty("versionId").GetGuid());
        Assert.True(offer.GetProperty("developmentFixture").GetBoolean());
        Assert.False(offer.GetProperty("paymentConfirmationIsTaxInvoice").GetBoolean());
        Assert.DoesNotContain("canary", offer.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.False(offer.TryGetProperty("accountingTreatmentReference", out _));
        Assert.False(offer.TryGetProperty("adminId", out _));
        using var replay = await P0202Fixture.SendAsync(client, assertion, path: CustomerPlans);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("Billing.DelegationReplayed", await P0206Fixture.CodeAsync(replay));
    }

    [Fact]
    public async Task MerchantCatalogue_BoundsEachPageAndSignsTheExactCursor()
    {
        using var signing = new P0201Fixture(database, "catalogue_paging_fixture");
        var settings = LocalSettings();
        foreach (var (key, value) in signing.LicensingSettings()) settings[key] = value;
        await using var fixture = await P0206Fixture.StartAsync(database, settings);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        using var client = fixture.Api.CreateClient();
        signing.Clock.Now = fixture.Clock.GetUtcNow();
        var actor = Guid.NewGuid();
        var trial = await PublishTrialAsync(manager);
        var ids = Enumerable.Range(0, PublishedCatalogueV1.PageSize + 1).Select(_ => Guid.NewGuid()).Order().ToArray();
        foreach (var id in ids)
        {
            var facts = PlanFacts(0);
            facts["name"] = string.Concat(Enumerable.Repeat("🧾", 40));
            var draft = await SavePlanAsync(manager, id, facts, HttpStatusCode.Created);
            var path = PlanPath(id, draft.GetProperty("versionId").GetGuid());
            var preview = await PrepareAsync(manager, path, Guid.NewGuid(), 1, trial);
            using var confirmed = await ConfirmAsync(manager, path, preview);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        }

        P0202Fixture.ActorAssertion Sign(string path) => P0202Fixture.Sign(signing, actor, PublishedCatalogueV1.Read, [],
            claims => claims["path"] = path);
        using var response = await P0202Fixture.SendAsync(client, Sign(CustomerPlans), path: CustomerPlans);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length <= PublishedCatalogueV1.MaxResponseBytes);
        var page = await P0206Fixture.BodyAsync(response);
        Assert.Equal(ids[..^1], page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("planId").GetGuid()));
        var cursor = page.GetProperty("nextCursor").GetGuid();
        Assert.Equal(ids[^2], cursor);
        var continuation = PublishedCatalogueV1.ReadPath(cursor);
        using var misbound = await P0202Fixture.SendAsync(client, Sign(CustomerPlans), path: continuation);
        Assert.Equal(HttpStatusCode.Unauthorized, misbound.StatusCode);
        using var last = await P0202Fixture.SendAsync(client, Sign(continuation), path: continuation);
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        var lastPage = await P0206Fixture.BodyAsync(last);
        Assert.Equal(ids[^1], Assert.Single(lastPage.GetProperty("items").EnumerateArray()).GetProperty("planId").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, lastPage.GetProperty("nextCursor").ValueKind);
    }
}
