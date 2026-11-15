using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P02-06: Licensing Managers draft versioned plans and the trial policy. A missing fact stays visibly
/// missing (never an unlimited default), every draft stays inactive, and nothing reaches merchant surfaces.
/// </summary>
public sealed partial class P0206Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Plans = P0206Fixture.Plans;
    private static readonly string[] MultiShopMissing = ["annualPricePaise", "includedShops", "membersPerShop",
        "extraShopsOffered", "customerTermsVersion", "refundPolicyVersion"];
    private static readonly string[] UnsetFacts = ["annualPricePaise", "includedShops", "membersPerShop",
        "extraShopsOffered", "extraShopMonthlyPricePaise", "extraShopAnnualPricePaise", "customerTermsVersion",
        "refundPolicyVersion", "negotiationReason"];

    [Fact]
    public async Task LicensingManagerReadsEmptyCatalogue_OwnerWithoutGrantIsRefused()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using (manager)
        {
            using var listed = await manager.SendAsync(HttpMethod.Get, Plans);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var body = await P0206Fixture.BodyAsync(listed);
            Assert.Empty(body.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCursor").ValueKind);
        }

        using var owner = await fixture.SignInOwnerAsync();
        using var refused = await owner.SendAsync(HttpMethod.Get, Plans);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.True(refused.Headers.CacheControl?.NoStore);
        using var anonymous = P0114Tests.RawClient(fixture.Api);
        using var unauthenticated = await anonymous.GetAsync(new Uri(Plans, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task IncompletePlanDraft_PersistsMissingFactsAndStaysDraft()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        using var created = await P0206Fixture.PutPlanAsync(manager, planId, new
        {
            expectedVersion = 0, kind = "multi-shop", name = "Growth (local fixture)", monthlyPricePaise = 149900L,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var savedAt = fixture.Clock.GetUtcNow();

        using var listed = await manager.SendAsync(HttpMethod.Get, Plans);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var item = Assert.Single((await P0206Fixture.BodyAsync(listed)).GetProperty("items").EnumerateArray());
        Assert.Equal(planId, item.GetProperty("planId").GetGuid());
        Assert.Equal("multi-shop", item.GetProperty("kind").GetString());
        var draft = item.GetProperty("draft");
        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal(1, draft.GetProperty("version").GetInt32());
        Assert.Equal(1, draft.GetProperty("versionNumber").GetInt32());
        Assert.Equal("INR", draft.GetProperty("currency").GetString());
        Assert.Equal(149900L, draft.GetProperty("monthlyPricePaise").GetInt64());
        foreach (var name in UnsetFacts)
            Assert.Equal(JsonValueKind.Null, draft.GetProperty(name).ValueKind);
        Assert.False(draft.GetProperty("complete").GetBoolean());
        Assert.Equal(MultiShopMissing, Missing(draft));
        Assert.Equal(savedAt, draft.GetProperty("updatedAt").GetDateTimeOffset());

        Assert.Equal(1L, await fixture.ScalarAsync($"""
            SELECT count(*) FROM plan_versions WHERE plan_id = '{planId}' AND status = 1 AND currency = 'INR'
              AND annual_price_paise IS NULL AND included_shops IS NULL AND members_per_shop IS NULL
              AND extra_shops_offered IS NULL AND customer_terms_version IS NULL AND monthly_price_paise = 149900
            """));
        Assert.Equal(1L, await fixture.ScalarAsync($"SELECT count(*) FROM catalogue_draft_changes WHERE target_id IN (SELECT id FROM plan_versions WHERE plan_id = '{planId}')"));
    }

    [Fact]
    public async Task PlanDrafts_PageFromTheLastReturnedPlanWithoutOverlap()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planIds = Enumerable.Range(0, 55).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in planIds)
            await SaveAsync(manager, id, MultiShop(0), HttpStatusCode.Created);

        using var first = await manager.SendAsync(HttpMethod.Get, Plans);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstPage = await P0206Fixture.BodyAsync(first);
        var firstIds = PageIds(firstPage);
        Assert.Equal(50, firstIds.Length);
        var cursor = firstPage.GetProperty("nextCursor").GetGuid();
        Assert.Equal(firstIds[^1], cursor);

        using var second = await manager.SendAsync(HttpMethod.Get, $"{Plans}?cursor={cursor:D}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondPage = await P0206Fixture.BodyAsync(second);
        var secondIds = PageIds(secondPage);
        Assert.Equal(5, secondIds.Length);
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("nextCursor").ValueKind);
        Assert.Equal(planIds.Order(), firstIds.Concat(secondIds));
    }

    private static Guid[] PageIds(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("planId").GetGuid()).ToArray();

    internal static string[] Missing(JsonElement draft) =>
        draft.GetProperty("missing").EnumerateArray().Select(value => value.GetString()!).ToArray();
}
