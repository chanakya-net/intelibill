using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Theory]
    [InlineData("Account", "Security")]
    [InlineData("Shop", "Trading")]
    public async Task ExplicitScopeIsSavedBeforeMerchantAcknowledgement(string scope, string mode)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var first = await CreateTradingShopAsync(fixture.Hosts);
        var second = await CreateTradingShopAsync(fixture.Hosts);
        var unrelated = await CreateTradingShopAsync(fixture.Hosts);
        if (scope == "Account")
            await GrantFixtureMembershipAsync(first, unrelated);
        var account = await LinkAsync(fixture, first, second);
        await LinkAsync(fixture, unrelated);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var before = await fixture.LatestSnapshotAsync(first.ShopId);
        await EnrollAsync(fixture, first, second, unrelated);
        var till = mode == "Trading" ? await OpenTillAsync(fixture, first) : (Guid.Empty, 0u);
        var sibling = await fixture.LatestSnapshotAsync(second.ShopId);
        var foreign = await fixture.LatestSnapshotAsync(unrelated.ShopId);

        using var security = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        Guid? shopId = scope == "Shop" ? first.ShopId : null;
        using var preview = await P0212Fixture.PostAsync(security, path + "/preview", new { scope, shopId });
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var previewBody = await preview.Content.ReadAsStringAsync();
        if (Environment.GetEnvironmentVariable("P0403_PREVIEW_EVIDENCE_DIR") is { Length: > 0 } directory)
            await File.WriteAllTextAsync(Path.Combine(directory, $"preview-{scope}.json"), previewBody);
        using var previewDocument = JsonDocument.Parse(previewBody);
        var impact = previewDocument.RootElement;
        var shops = impact.GetProperty("shops").EnumerateArray().ToArray();
        Assert.Equal(scope == "Shop" ? 1 : 2, shops.Length);
        Assert.All(shops, shop => Assert.Equal("Independent", shop.GetProperty("coverageMode").GetString()));

        var effectiveAt = fixture.Hosts.Fixture.Clock.Now.AddSeconds(10);
        var id = await SaveAsync(fixture, security, path, scope, shopId, mode, impact, effectiveAt);
        await AssertReadableAsync(first);
        await AssertCustomerExplanationAsync(first);
        if (scope == "Account")
            await AssertFencedDeliveryAsync(fixture, account, second.ShopId, await fixture.LatestSnapshotAsync(first.ShopId));
        await AssertPropagationAsync(security, path, id, "Pending");
        await fixture.DrainAsync(source: true);
        await AssertPropagationAsync(security, path, id, "Pending");
        fixture.Hosts.Fixture.Clock.Now = effectiveAt.AddMicroseconds(-1);
        await AssertReadableAsync(first);
        fixture.Hosts.Fixture.Clock.Now = effectiveAt;
        await AssertAccessAsync(first, second, unrelated, mode, till);
        await fixture.DrainAsync(source: false);
        await AssertPropagationAsync(security, path, id, "Acknowledged");
        if (scope == "Account")
            await AssertExplicitShopDeniedAsync(first, unrelated);

        await AssertHeadsAsync(fixture, scope, before, sibling, foreign);
    }

    private static async Task AssertHeadsAsync(P0213Fixture fixture, string scope, EntitlementSnapshotV1 before,
        EntitlementSnapshotV1 sibling, EntitlementSnapshotV1 foreign)
    {
        var first = before.ShopId;
        var second = sibling.ShopId;
        var unrelated = foreign.ShopId;
        var after = await fixture.LatestSnapshotAsync(first);
        Assert.True(after.Shop!.Revision > before.Shop!.Revision);
        var siblingAfter = await fixture.LatestSnapshotAsync(second);
        if (scope == "Account")
        {
            Assert.True(after.Account!.Revision > before.Account!.Revision);
            Assert.Equal("Security", after.Account.Restriction);
            Assert.Equal(after.Account, siblingAfter.Account);
        }
        else
        {
            Assert.Equal(before.Account, after.Account);
            Assert.Equal(sibling, siblingAfter);
        }
        Assert.Equal(foreign, await fixture.LatestSnapshotAsync(unrelated));
        var (replayHttp, replay) = await fixture.SendAsync(before);
        replayHttp.Dispose();
        Assert.NotNull(replay);
        var withoutRestriction = after with { EventId = Guid.NewGuid(),
            Shop = after.Shop with { Revision = after.Shop.Revision + 1, Suspension = null } };
        var (http, ack) = await fixture.SendAsync(withoutRestriction);
        http.Dispose();
        Assert.Equal(scope == "Shop" ? "ConflictingShopScope" : "Applied", ack?.Disposition);
    }

    private static async Task<Guid> SaveAsync(P0213Fixture fixture, P0110CookieBrowser security, string path, string scope,
        Guid? shopId, string mode, JsonElement impact, DateTimeOffset effectiveAt)
    {
        var command = JsonNode.Parse(impact.GetProperty("selection").GetRawText())!.AsObject();
        Assert.Equal(scope, (string?)command["scope"]);
        Assert.Equal(shopId?.ToString(), (string?)command["shopId"]);
        command["mode"] = mode;
        command["operationKey"] = Guid.NewGuid();
        command["expectedVersion"] = 0;
        command["impactDigest"] = impact.GetProperty("impactDigest").GetString();
        command["customerExplanation"] = "Contact support about shop access.";
        const string internalReason = "Internal review detail.";
        const string reviewReference = "CASE-878";
        command["internalReason"] = internalReason;
        command["reviewReference"] = reviewReference;
        command["effectiveAt"] = effectiveAt;
        using var platform = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using var denied = await P0212Fixture.PostAsync(platform, path, command);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var saved = await P0212Fixture.PostAsync(security, path, command);
        var body = await saved.Content.ReadAsStringAsync();
        Assert.True(saved.StatusCode == HttpStatusCode.Created, body);
        Assert.DoesNotContain(internalReason, body);
        Assert.DoesNotContain(reviewReference, body);
        using var document = JsonDocument.Parse(body);
        var id = document.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(shopId?.ToString(), document.RootElement.GetProperty("shopId").GetString());
        Assert.Equal(mode, document.RootElement.GetProperty("mode").GetString());
        using var repeated = await P0212Fixture.PostAsync(security, path, command);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(id, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var staleCommand = command.DeepClone().AsObject();
        staleCommand["operationKey"] = Guid.NewGuid();
        using var staleVersion = await P0212Fixture.PostAsync(security, path, staleCommand);
        Assert.Equal(HttpStatusCode.Conflict, staleVersion.StatusCode);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_restrictions"));
        Assert.Equal(internalReason, await fixture.SourceScalarAsync($"SELECT internal_reason FROM operational_restrictions WHERE id = '{id:D}'"));
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using var staleMfa = await P0212Fixture.PostAsync(security, path, command);
        Assert.Equal("Suspension.RecentMfaRequired", await P0206Fixture.CodeAsync(staleMfa));
        return id;
    }

    private async Task GrantFixtureMembershipAsync(Merchant target, Merchant other)
    {
        await using var db = MerchantDb(database);
        Assert.False(db.Database.HasPendingModelChanges());
        db.ShopMemberships.Add(ShopMembership.Create(target.ShopId, other.UserId, ShopRole.Staff, false));
        await db.SaveChangesAsync();
        using var permitted = await other.Client.GetAsync($"/api/shops/{target.ShopId:D}");
        Assert.True(permitted.StatusCode == HttpStatusCode.OK, await permitted.Content.ReadAsStringAsync());
    }

    private static async Task AssertExplicitShopDeniedAsync(Merchant target, Merchant other)
    {
        using var denied = await other.Client.GetAsync($"/api/shops/{target.ShopId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(denied));
        using var permitted = await other.Client.GetAsync($"/api/shops/{other.ShopId:D}");
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
    }

    private static async Task AssertCustomerExplanationAsync(Merchant owner)
    {
        using var response = await ShopStatusAsync(owner.Client, owner.ShopId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains("Contact support about shop access.", body);
        Assert.DoesNotContain("Internal review detail.", body);
        Assert.DoesNotContain("CASE-878", body);
    }

    internal static async Task EnrollAsync(P0213Fixture fixture, params Merchant[] shops)
    {
        foreach (var shop in shops)
        {
            var activated = await fixture.MerchantScalarAsync($"""
                UPDATE shop_license_states SET enforcement_activated_at = now() WHERE shop_id = '{shop.ShopId:D}'
                RETURNING shop_id
                """);
            Assert.Equal(shop.ShopId, activated);
        }
    }

    private static async Task AssertReadableAsync(Merchant shop)
    {
        using var history = await shop.Client.GetAsync("/api/sales");
        Assert.True(history.StatusCode == HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
    }

    private static async Task AssertAccessAsync(Merchant first, Merchant second, Merchant unrelated, string mode,
        (Guid Session, uint Version) till)
    {
        await AssertReadableAsync(unrelated);
        if (mode == "Trading")
        {
            await AssertReadableAsync(first);
            await AssertReadableAsync(second);
            using var close = await first.Client.PostAsJsonAsync($"/api/tills/sessions/{till.Session:D}/close",
                new { countedCash = 50, expectedVersion = till.Version, operationKey = Guid.NewGuid().ToString("N") });
            Assert.True(close.StatusCode == HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
            using var newTill = await first.Client.PostAsJsonAsync("/api/tills", new { name = "Another till", operationKey = "new-till" });
            Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(newTill));
            return;
        }

        foreach (var shop in new[] { first, second })
        foreach (var route in new[] { "/api/sales", "/api/exports/sales", "/api/offline-reconciliation" })
        {
            using var denied = await shop.Client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(denied));
        }
    }

    internal static async Task<(Guid, uint)> OpenTillAsync(P0213Fixture fixture, Merchant shop)
    {
        var options = fixture.Hosts.Merchant.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true;
        options.AllowedShopIds = [shop.ShopId];
        using var settings = await shop.Client.PutAsJsonAsync($"/api/shops/{shop.ShopId:D}/till-settings",
            new { enabled = true, expectedVersion = 0, reason = "Fixture drawer setup", confirmed = true });
        Assert.True(settings.StatusCode == HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        using var created = await shop.Client.PostAsJsonAsync("/api/tills",
            new { name = "Fixture till", operationKey = Guid.NewGuid().ToString("N") });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var tillId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var opened = await shop.Client.PostAsJsonAsync($"/api/tills/{tillId:D}/sessions",
            new { cashierId = shop.UserId, openingFloat = 50, operationKey = Guid.NewGuid().ToString("N") });
        Assert.True(opened.StatusCode == HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        var session = await opened.Content.ReadFromJsonAsync<JsonElement>();
        return (session.GetProperty("id").GetGuid(), session.GetProperty("version").GetUInt32());
    }

    private static async Task AssertFencedDeliveryAsync(P0213Fixture fixture, Guid account, Guid sibling,
        EntitlementSnapshotV1 snapshot)
    {
        await using var connection = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var acquire = new NpgsqlCommand("SELECT public.take_merchant_licence_fence($1, $2, true)", connection, transaction);
        acquire.Parameters.AddWithValue(account);
        acquire.Parameters.AddWithValue(sibling);
        await acquire.ExecuteNonQueryAsync();

        var delivery = fixture.SendAsync(snapshot);
        var blocked = false;
        for (var attempt = 0; attempt < 40 && !delivery.IsCompleted; attempt++)
        {
            blocked = (bool)(await fixture.MerchantScalarAsync($"""
                SELECT EXISTS (SELECT FROM pg_stat_activity WHERE {connection.ProcessID} = ANY(pg_blocking_pids(pid)))
                """))!;
            if (blocked) break;
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        await transaction.CommitAsync();
        var (http, ack) = await delivery;
        http.Dispose();
        Assert.True(blocked);
        Assert.Equal("Applied", ack?.Outcome);
        Assert.Equal(snapshot.Account!.Revision, ack?.AppliedAccountRevision);
    }

    internal static async Task<JsonElement> AssertPropagationAsync(P0110CookieBrowser security, string path, Guid id, string expected)
    {
        using var response = await security.SendAsync(HttpMethod.Get, $"{path}/{id:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, status.GetProperty("propagation").GetString());
        return status;
    }

    internal static async Task<Guid> LinkAsync(P0213Fixture fixture, Merchant payer, params Merchant[] otherShops)
    {
        var account = await CreateAccountAsync(payer.Client);
        foreach (var shop in otherShops.Prepend(payer))
            await LinkIndependentAsync(fixture, account, payer, shop);
        return account;
    }

    private static async Task LinkIndependentAsync(P0213Fixture fixture, Guid account, Merchant payer, Merchant shop)
    {
        var now = fixture.Hosts.StatusClock.Now.ToOffset(TimeSpan.FromHours(5.5));
        var anchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var term = await SeedTermAsync(fixture.Hosts, account, new TermSeed(anchor.ToUniversalTime(),
            anchor.AddMonths(1).ToUniversalTime(), AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
        await P0213Fixture.LinkAsync(account, payer, shop, term);
    }

    internal static async Task<P0110CookieBrowser> SignInSecurityAsync(P0213Fixture fixture, params string[] alsoGranted)
    {
        const string email = "suspension.security@example.test";
        var catalogue = fixture.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        var seed = await catalogue.EnrollOperatorAsync(owner, email);
        await catalogue.GrantAsync(email, ["security-administrator", .. alsoGranted]);
        return await catalogue.SignInOperatorAsync(seed, email);
    }
}
