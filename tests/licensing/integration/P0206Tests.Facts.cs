using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0206Tests
{
    private static readonly string[] SingleShopMissing = ["annualPricePaise", "membersPerShop", "customerTermsVersion",
        "refundPolicyVersion"];
    private static readonly string[] DraftMembers = ["versionId", "versionNumber", "status", "version", "name", "currency",
        "monthlyPricePaise", "annualPricePaise", "includedShops", "membersPerShop", "extraShopsOffered",
        "extraShopMonthlyPricePaise", "extraShopAnnualPricePaise", "customerTermsVersion", "refundPolicyVersion",
        "negotiationReason", "missing", "complete", "updatedAt"];
    private static readonly string[] CatalogueColumns = ["active_member_cap", "actor_admin_id", "annual_price_paise",
        "created_at", "created_by_admin_id", "currency", "customer_terms_version", "duration_days",
        "extra_shop_annual_price_paise", "extra_shop_monthly_price_paise", "extra_shops_offered", "id", "included_shops",
        "kind", "max_concurrent_trial_shops", "members_per_shop", "monthly_price_paise", "name", "negotiation_reason",
        "occurred_at", "plan_id", "reason", "refund_policy_version", "revision", "status", "target_id", "target_kind",
        "trial_terms_version", "updated_at", "updated_by_admin_id", "version_number"];

    /// <summary>A complete multi-shop body; overrides replace (or, with a null value, blank) single members.</summary>
    internal static Dictionary<string, object?> MultiShop(int expectedVersion, params (string Name, object? Value)[] overrides)
    {
        var body = new Dictionary<string, object?>
        {
            ["expectedVersion"] = expectedVersion, ["kind"] = "multi-shop", ["name"] = "Growth (local fixture)",
            ["monthlyPricePaise"] = 149900L, ["annualPricePaise"] = 1499000L, ["includedShops"] = 3,
            ["membersPerShop"] = 5, ["extraShopsOffered"] = true, ["extraShopMonthlyPricePaise"] = 39900L,
            ["extraShopAnnualPricePaise"] = 399000L, ["customerTermsVersion"] = "terms-2026.11",
            ["refundPolicyVersion"] = "refund-2026.11",
        };
        foreach (var (name, value) in overrides) body[name] = value;
        return body;
    }

    [Fact]
    public async Task ExplicitZeroPriceIsRecorded_MissingStaysMissing_CompleteDraftStaysInactive()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        await SaveAsync(manager, planId, MultiShop(0, ("annualPricePaise", null), ("membersPerShop", null)), HttpStatusCode.Created);
        var zero = await SaveAsync(manager, planId, MultiShop(1, ("monthlyPricePaise", 0L), ("membersPerShop", null)));
        Assert.Equal(0L, zero.GetProperty("monthlyPricePaise").GetInt64());
        Assert.Equal(["membersPerShop"], Missing(zero));
        Assert.False(zero.GetProperty("complete").GetBoolean());

        var complete = await SaveAsync(manager, planId, MultiShop(2, ("monthlyPricePaise", 0L)));
        Assert.Empty(Missing(complete));
        Assert.True(complete.GetProperty("complete").GetBoolean());
        Assert.Equal("draft", complete.GetProperty("status").GetString());
        Assert.Equal(3, complete.GetProperty("version").GetInt32());
        Assert.Equal(1L, await fixture.ScalarAsync($"""
            SELECT count(*) FROM plan_versions WHERE plan_id = '{planId}' AND status = 1 AND monthly_price_paise = 0
            """));

        var single = await SaveAsync(manager, Guid.NewGuid(), new
        {
            expectedVersion = 0, kind = "single-shop", name = "Solo (local fixture)", monthlyPricePaise = 49900L,
        }, HttpStatusCode.Created);
        Assert.Equal(1, single.GetProperty("includedShops").GetInt32());
        Assert.False(single.GetProperty("extraShopsOffered").GetBoolean());
        Assert.Equal(SingleShopMissing, Missing(single));
    }

    [Fact]
    public async Task InvalidOrUnlimitedValues_AreRefusedWithoutWriting()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        await SaveAsync(manager, planId, MultiShop(0), HttpStatusCode.Created);
        var path = $"{Plans}/{planId}/draft";
        var refusals = new (Dictionary<string, object?> Body, string Code)[]
        {
            (MultiShop(1, ("monthlyPricePaise", -1L)), "Catalogue.AmountInvalid"),
            (MultiShop(1, ("annualPricePaise", 1_000_000_000_001L)), "Catalogue.AmountInvalid"),
            (MultiShop(1, ("membersPerShop", 0)), "Catalogue.CapacityInvalid"),
            (MultiShop(1, ("membersPerShop", -1)), "Catalogue.CapacityInvalid"),
            (MultiShop(1, ("includedShops", 100_001)), "Catalogue.CapacityInvalid"),
            (MultiShop(1, ("includedShops", 1)), "Catalogue.CapacityInvalid"),
            (MultiShop(1, ("name", "   ")), "Catalogue.NameInvalid"),
            (MultiShop(1, ("name", new string('n', 81))), "Catalogue.NameInvalid"),
            (MultiShop(1, ("customerTermsVersion", "terms 1")), "Catalogue.PolicyVersionInvalid"),
            (MultiShop(1, ("extraShopsOffered", false)), "Catalogue.ExtraShopTermsInvalid"),
            (MultiShop(1, ("kind", "enterprise")), "Catalogue.DraftInvalid"),
            (MultiShop(1, ("expectedVersion", null)), "Catalogue.DraftInvalid"),
        };
        foreach (var (body, code) in refusals)
        {
            using var refused = await P0206Fixture.PutPlanAsync(manager, planId, body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(code, await P0206Fixture.CodeAsync(refused));
        }
        foreach (var json in new[]
                 {
                     """{"expectedVersion":1,"kind":"multi-shop","name":"Growth","monthlyPricePaise":"149900"}""",
                     """{"expectedVersion":1,"kind":"multi-shop","name":"Growth","monthlyPricePaise":1499.5}""",
                     """{"expectedVersion":1,"kind":"multi-shop","name":"Growth","membersPerShop":"unlimited"}""",
                     """{"expectedVersion":1,"kind":"multi-shop","name":"Growth","name":"Other"}""",
                     """{"expectedVersion":1,"kind":"multi-shop","name":"Growth",""",
                 })
        {
            using var malformed = await P0206Fixture.PutRawAsync(manager, path, json);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            Assert.Equal("Catalogue.DraftInvalid", await P0206Fixture.CodeAsync(malformed));
        }
        using var kindChange = await P0206Fixture.PutPlanAsync(manager, planId, MultiShop(1, ("kind", "custom"), ("reason", "Negotiated"),
            ("extraShopsOffered", null), ("extraShopMonthlyPricePaise", null), ("extraShopAnnualPricePaise", null)));
        Assert.Equal(HttpStatusCode.Conflict, kindChange.StatusCode);
        Assert.Equal("Catalogue.PlanKindFixed", await P0206Fixture.CodeAsync(kindChange));
        using var oversized = await P0206Fixture.PutPlanAsync(manager, planId, MultiShop(1, ("name", new string('x', 5000))));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);

        Assert.Equal(1, await fixture.ScalarAsync($"SELECT revision FROM plan_versions WHERE plan_id = '{planId}'"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
    }

    [Fact]
    public async Task ContractCarriesOnlyApprovedCommercialFacts()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        var saved = await SaveAsync(manager, planId, MultiShop(0), HttpStatusCode.Created);
        Assert.Equal(DraftMembers.Order(), saved.EnumerateObject().Select(member => member.Name).Order());
        foreach (var upsell in new[] { "invoiceQuota", "seatPricePaise", "features", "setupFeePaise", "meteredSeats" })
        {
            using var refused = await P0206Fixture.PutPlanAsync(manager, planId, MultiShop(1, (upsell, 1)));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("Catalogue.DraftInvalid", await P0206Fixture.CodeAsync(refused));
        }
        var columns = (string)(await fixture.ScalarAsync("""
            SELECT string_agg(DISTINCT column_name, ',' ORDER BY column_name) FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('plans', 'plan_versions', 'trial_policy_versions', 'catalogue_draft_changes')
            """))!;
        Assert.Equal(CatalogueColumns, columns.Split(','));
    }

    [Fact]
    public async Task CustomTerms_RequireRecordedReason()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        object Custom(int version, string? reason) => new
        {
            expectedVersion = version, kind = "custom", name = "Chain negotiated (local fixture)",
            annualPricePaise = 5000000L, includedShops = 12, membersPerShop = 8, reason,
        };
        foreach (var reason in new[] { null, "   " })
        {
            using var refused = await P0206Fixture.PutPlanAsync(manager, planId, Custom(0, reason));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("Catalogue.CustomReasonRequired", await P0206Fixture.CodeAsync(refused));
        }
        var saved = await SaveAsync(manager, planId, Custom(0, " Twelve-shop chain asked for annual terms "), HttpStatusCode.Created);
        Assert.Equal("Twelve-shop chain asked for annual terms", saved.GetProperty("negotiationReason").GetString());
        Assert.Equal(["customerTermsVersion", "refundPolicyVersion"], Missing(saved));
        await SaveAsync(manager, planId, Custom(1, "Renewed negotiation"));
        Assert.Equal(2L, await fixture.ScalarAsync($"""
            SELECT count(*) FROM catalogue_draft_changes c JOIN plan_versions v ON v.id = c.target_id
            WHERE v.plan_id = '{planId}' AND c.reason IS NOT NULL
            """));

        using var publicReason = await P0206Fixture.PutPlanAsync(manager, Guid.NewGuid(), MultiShop(0, ("reason", "Discount")));
        Assert.Equal(HttpStatusCode.BadRequest, publicReason.StatusCode);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM plans"));
    }

    internal static async Task<JsonElement> SaveAsync(P0110CookieBrowser manager, Guid planId, object body,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await P0206Fixture.PutPlanAsync(manager, planId, body);
        Assert.Equal(expected, response.StatusCode);
        return (await P0206Fixture.BodyAsync(response)).GetProperty("draft");
    }

    [Theory]
    [InlineData("multi-shop", "a\u0000b", "Catalogue.NameInvalid")]
    [InlineData("multi-shop", "a\nb", "Catalogue.NameInvalid")]
    [InlineData("multi-shop", "a\t", "Catalogue.NameInvalid")]
    [InlineData("custom", "a\u0000b", "Catalogue.CustomReasonRequired")]
    [InlineData("custom", "a\r\nb", "Catalogue.CustomReasonRequired")]
    [InlineData("custom", "\r\na", "Catalogue.CustomReasonRequired")]
    public async Task ControlCharacters_AreRefusedBeforeDraftOrChangeRowsAreCreated(string kind, string text, string code)
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        using var refused = await P0206Fixture.PutPlanAsync(manager, Guid.NewGuid(), new
        {
            expectedVersion = 0, kind, name = kind == "custom" ? "Negotiated plan" : text,
            reason = kind == "custom" ? text : null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(code, await P0206Fixture.CodeAsync(refused));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM plans"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM plan_versions"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_draft_changes"));
    }
}
