using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    [Fact]
    public void TransportContractContainsOnlyOperationalFields()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/entitlement-snapshot-v1.json")));
        var root = fixture.RootElement;
        var vector = root.GetProperty("snapshot");
        var snapshot = vector.Deserialize<EntitlementSnapshotV1>(EntitlementSnapshotsV1.Json)!;
        Assert.True(snapshot.HasValidIdentity());
        Assert.True(snapshot.Shop!.IsValid());
        Assert.Null(EntitlementSnapshotsV1.ReadRestrictionError(snapshot.Account!));
        Assert.Equal(EntitlementSnapshotsV1.BodyLimit, root.GetProperty("maximumBodyBytes").GetInt32());
        Assert.Equal(root.GetProperty("freshnessHours").GetDouble(), EntitlementSnapshotsV1.Freshness.TotalHours);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(vector.GetRawText()), JsonNode.Parse(serialized)));

        var acknowledgement = new EntitlementAcknowledgementV1(1, snapshot.EventId, SignedServiceAssertion.Digest(serialized),
            snapshot.AccountId, snapshot.ShopId, snapshot.AffiliationRequestId, 8, 5, 8, 6, "Applied", "Superseded", P0210Fixture.Start);
        AssertFields(root.GetProperty("acknowledgementFields"), acknowledgement);
        var reconciliation = new EntitlementReconciliationV1(1, Guid.NewGuid(), snapshot.AccountId, snapshot.ShopId,
            snapshot.AffiliationRequestId, 8, 6);
        AssertFields(root.GetProperty("reconciliationFields"), reconciliation);
    }

    private static void AssertFields<T>(JsonElement vector, T contract)
    {
        var actual = JsonSerializer.SerializeToElement(contract, EntitlementSnapshotsV1.Json)
            .EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal);
        Assert.Equal(vector.EnumerateArray().Select(item => item.GetString()).Order(StringComparer.Ordinal), actual);
    }
}
