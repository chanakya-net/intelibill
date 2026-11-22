using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Fact]
    public void ReopeningCutoffIsOptionalAndBounded()
    {
        using var fixture = Load("entitlement-snapshot-v1");
        var root = fixture.RootElement;
        var vector = root.GetProperty("snapshot");
        var previous = vector.Deserialize<EntitlementSnapshotV1>(EntitlementSnapshotsV1.Json)!;
        var proof = root.GetProperty("reopenProof");
        Assert.Equal(proof.GetProperty("legacyPayloadSha256").GetString(), SignedServiceAssertion.Digest(
            JsonSerializer.SerializeToUtf8Bytes(previous, EntitlementSnapshotsV1.Json)));
        var shop = previous.Shop! with
        {
            Revision = proof.GetProperty("revision").GetInt64(), ReopenedRevision = proof.GetProperty("reopenedRevision").GetInt64(),
            Basis = "Paid", ClosureRevision = proof.GetProperty("closureRevision").GetInt64(),
        };
        var snapshot = previous with { Shop = shop };
        Assert.True(shop.IsValid());
        Assert.Equal(snapshot, JsonSerializer.Deserialize<EntitlementSnapshotV1>(
            JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json), EntitlementSnapshotsV1.Json));
        foreach (var invalid in proof.GetProperty("invalidCutoffs").EnumerateArray())
            Assert.False((shop with { ReopenedRevision = invalid.GetInt64() }).IsValid());
        foreach (var invalid in new[] { 0, 1, shop.ReopenedRevision!.Value, shop.Revision + 1 })
            Assert.False((shop with { ClosureRevision = invalid }).IsValid());
    }

    [Fact]
    public void ProviderClosureWireAddsReopeningWithoutDisclosingConsent()
    {
        using var fixture = Load("shop-closure-v1");
        var root = fixture.RootElement;
        var shop = new ProviderClosureShopV1(Guid.NewGuid(), "Fixture shop", "Applied", 2, 2, null);
        var retainedFields = root.GetProperty("providerTargetFields").EnumerateArray().Select(item => item.GetString());
        Assert.Equal(retainedFields, JsonSerializer.SerializeToElement(shop, EntitlementSnapshotsV1.Json)
            .EnumerateObject().Select(property => property.Name));
        shop = shop with { Reopening = new(Guid.NewGuid(), P0202Fixture.WholeSecondNow(), "Pending") };
        var published = JsonSerializer.SerializeToElement(shop, EntitlementSnapshotsV1.Json).GetProperty("reopening");
        Assert.Equal(root.GetProperty("providerReopeningFields").EnumerateArray().Select(item => item.GetString()),
            published.EnumerateObject().Select(property => property.Name));
    }

    private static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root, $"tests/contracts/licensing/{name}.json")));
}
