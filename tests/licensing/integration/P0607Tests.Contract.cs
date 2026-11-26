using System.Text;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public void SignedTransferContractBindsPhaseAndCommandBytes()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/coverage-transfer-approval-v1.json")));
        var json = fixture.RootElement.GetProperty("command").GetRawText();
        var command = JsonSerializer.Deserialize<TransferCommandV1>(json, EntitlementSnapshotsV1.Json)!;
        var receipt = fixture.RootElement.GetProperty("receipt").Deserialize<TransferReceiptV1>(EntitlementSnapshotsV1.Json)!;
        Assert.True(command.IsValid());
        Assert.True(receipt.IsValid());
        Assert.Equal(SignedServiceAssertion.Digest(Encoding.UTF8.GetBytes(json)), receipt.CommandDigest);
        Assert.Equal(command.EventId, receipt.EventId);
        Assert.False((command with { CutoverAt = command.PaidThroughAt }).IsValid());
        Assert.False((command with { PrepareDigest = null }).IsValid());
        Assert.False((receipt with { Phase = CoverageTransferApprovalsV1.Prepare }).IsValid());
        Assert.False((receipt with { Outcome = "Refused" }).IsValid());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TransferCommandV1>(json[..^1] + ",\"more\":true}", EntitlementSnapshotsV1.Json));
    }
}
