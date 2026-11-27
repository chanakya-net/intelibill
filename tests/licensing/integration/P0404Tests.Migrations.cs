using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests
{
    [Fact]
    public async Task ReleasedRestrictionEvidenceIsForcedRlsAndRetained()
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);

        await SubmitReleaseAsync(admin, $"{path}/{id:D}/release");
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        Assert.Equal("Acknowledged", await ReleasePropagationAsync(admin, path, id));
        await AssertReleaseRetentionAsync(fixture);
    }

    private static async Task AssertReleaseRetentionAsync(P0213Fixture fixture)
    {
        Assert.Equal(2L, await fixture.SourceScalarAsync("""
            SELECT count(*) FROM pg_class WHERE relname IN ('operational_restriction_releases',
                'operational_restriction_release_deliveries') AND relforcerowsecurity
            """));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.operational_restriction_releases"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.active_operational_restrictions"));
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(
            fixture.Hosts.LicensingOwner, "TRUNCATE public.operational_restriction_release_deliveries"));
        Assert.Equal("55000", immutable.SqlState);
    }

    [Fact]
    public void SignedReleaseMarkersAreOptionalAndScopeSpecific()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/entitlement-snapshot-v1.json")));
        var root = contract.RootElement;
        var vector = root.GetProperty("snapshot");
        var old = vector.Deserialize<EntitlementSnapshotV1>(EntitlementSnapshotsV1.Json)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(vector.GetRawText()),
            JsonSerializer.SerializeToNode(old, EntitlementSnapshotsV1.Json)));
        var release = root.GetProperty("releaseProof");
        var shop = release.GetProperty("releasedShop");
        var account = release.GetProperty("replacementAccount");
        var updated = old with
        {
            Shop = old.Shop! with { Revision = shop.GetProperty("revision").GetInt64(),
                ReleasedRevision = shop.GetProperty("releasedRevision").GetInt64() },
            Account = old.Account! with { Revision = account.GetProperty("revision").GetInt64(),
                PreviousReleaseRevision = account.GetProperty("previousReleaseRevision").GetInt64() },
        };
        Assert.Equal(updated, JsonSerializer.Deserialize<EntitlementSnapshotV1>(
            JsonSerializer.SerializeToUtf8Bytes(updated, EntitlementSnapshotsV1.Json), EntitlementSnapshotsV1.Json));
        Assert.True(updated.Shop.IsValid());
        Assert.Null(EntitlementSnapshotsV1.ReadRestrictionError(updated.Account));
        foreach (var invalid in release.GetProperty("invalidCutoffs").EnumerateArray())
        {
            Assert.False((updated.Shop with { ReleasedRevision = invalid.GetInt64() }).IsValid());
            Assert.Equal("InvalidAccountScope", EntitlementSnapshotsV1.ReadRestrictionError(
                updated.Account with { PreviousReleaseRevision = invalid.GetInt64() }));
        }
    }
}
