using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ReleaseSpecificSuspensionRecordsImmutableEvidence()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);

        var releasePath = $"{path}/{id:D}/release";
        var command = await PreviewReleaseAsync(admin, releasePath);
        using var saved = await P0212Fixture.PostAsync(admin, releasePath, command);
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        using var repeated = await P0212Fixture.PostAsync(admin, releasePath, command);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_restrictions"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_restriction_releases"));
        var release = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("release");
        var replay = (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("release");
        Assert.Equal(release.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
        Assert.Equal(2, release.GetProperty("version").GetInt64());
        Assert.NotEqual(Guid.Empty, release.GetProperty("releasedBy").GetGuid());
        Assert.Equal(fixture.Hosts.StatusClock.Now, release.GetProperty("releasedAt").GetDateTimeOffset());
        Assert.Equal(command.Reason, await fixture.SourceScalarAsync(
            $"SELECT reason FROM operational_restriction_releases WHERE restriction_id = '{id:D}'"));
        using var csrf = await admin.SendAsync(HttpMethod.Post, releasePath, command);
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using var invalid = await P0212Fixture.PostAsync(admin, releasePath, command with { Reason = "  " });
        Assert.Equal("Suspension.InvalidRelease", await P0206Fixture.CodeAsync(invalid));
        using var foreign = await P0212Fixture.PostAsync(admin, $"{path}/{Guid.NewGuid():D}/release", command);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using var stale = await P0212Fixture.PostAsync(admin, releasePath, command);
        Assert.Equal("Suspension.RecentMfaRequired", await P0206Fixture.CodeAsync(stale));
    }

    private sealed record ReleaseRequest(long ExpectedVersion, string? ImpactDigest, Guid OperationKey, string Reason);

    private static async Task<ReleaseRequest> PreviewReleaseAsync(P0110CookieBrowser admin, string path)
    {
        var impact = await ReadReleasePreviewAsync(admin, path);
        return Command(impact);
    }

    private static ReleaseRequest Command(JsonElement impact) => new(impact.GetProperty("version").GetInt64(),
        impact.GetProperty("impactDigest").GetString(), Guid.NewGuid(), "Security review completed.");

    private static async Task<JsonElement> ReadReleasePreviewAsync(P0110CookieBrowser admin, string path)
    {
        using var preview = await P0212Fixture.PostAsync(admin, path + "/preview", new { });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        return await preview.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> LinkAsync(P0213Fixture fixture, Merchant shop, DateTimeOffset? paidThrough = null,
        Guid? existingAccount = null, Merchant? payer = null)
    {
        var account = existingAccount ?? await CreateAccountAsync(shop.Client);
        var now = fixture.Hosts.StatusClock.Now.ToOffset(TimeSpan.FromHours(5.5));
        var end = (paidThrough ?? new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(1))
            .ToOffset(TimeSpan.FromHours(5.5));
        var anchor = end.AddMonths(-1);
        var term = await SeedTermAsync(fixture.Hosts, account, new TermSeed(anchor.ToUniversalTime(),
            end.ToUniversalTime(), AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
        await P0213Fixture.LinkAsync(account, payer ?? shop, shop, term);
        return account;
    }

    private static async Task<Guid> SuspendAsync(P0110CookieBrowser admin, string path, string scope, Guid? shopId, string mode = "Security")
    {
        using var preview = await P0212Fixture.PostAsync(admin, path + "/preview", new { scope, shopId });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var impact = await preview.Content.ReadFromJsonAsync<JsonElement>();
        using var saved = await P0212Fixture.PostAsync(admin, path, new
        {
            scope, shopId, mode, expectedVersion = impact.GetProperty("version").GetInt64(),
            impactDigest = impact.GetProperty("impactDigest").GetString(), operationKey = Guid.NewGuid(),
            customerExplanation = "Contact support about shop access.",
            internalReason = "Security review.", reviewReference = "CASE-879"
        });
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        return (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<P0110CookieBrowser> SignInSecurityAsync(P0213Fixture fixture, params string[] alsoGranted)
    {
        const string email = "release.security@example.test";
        var catalogue = fixture.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        var seed = await catalogue.EnrollOperatorAsync(owner, email);
        await catalogue.GrantAsync(email, ["security-administrator", .. alsoGranted]);
        return await catalogue.SignInOperatorAsync(seed, email);
    }
}
