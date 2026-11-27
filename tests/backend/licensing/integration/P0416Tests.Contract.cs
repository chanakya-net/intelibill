using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public void VersionOneClosureEvidenceKeepsCommercialStateSeparate()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/shop-closure-v1.json")));
        var root = fixture.RootElement;
        var vector = root.GetProperty("validation");
        var request = vector.Deserialize<ClosureValidationV1>(EntitlementSnapshotsV1.Json)!;
        Assert.Equal(ShopClosuresV1.ValidationPath, root.GetProperty("validationPath").GetString());
        Assert.Equal(ShopClosuresV1.FinancialTreatment, root.GetProperty("financialTreatment").GetString());
        Assert.Equal(request.ScopeDigest, SignedServiceAssertion.Digest(
            JsonSerializer.SerializeToUtf8Bytes(request.Targets, EntitlementSnapshotsV1.Json)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(vector.GetRawText()),
            JsonSerializer.SerializeToNode(request, EntitlementSnapshotsV1.Json)));

        var target = request.Targets[0];
        var evidence = new ProviderClosureShopV1(target.ShopId, target.ShopName, "Pending", 3, null, null);
        var provider = JsonSerializer.SerializeToElement(evidence, EntitlementSnapshotsV1.Json);
        Assert.Equal(root.GetProperty("providerTargetFields").EnumerateArray().Select(item => item.GetString()),
            provider.EnumerateObject().Select(item => item.Name));

        using var legacy = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/entitlement-snapshot-v1.json")));
        var original = legacy.RootElement.GetProperty("snapshot");
        var snapshot = original.Deserialize<EntitlementSnapshotV1>(EntitlementSnapshotsV1.Json)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.GetRawText()),
            JsonSerializer.SerializeToNode(snapshot, EntitlementSnapshotsV1.Json)));
        var closed = snapshot.Shop! with { State = root.GetProperty("operationalState").GetString()! };
        Assert.True(closed.IsValid());
        Assert.DoesNotContain(closed.State, SubscriptionStatusV1.States);
        Assert.Equal(snapshot.Shop.TermId, closed.TermId);
    }
}
