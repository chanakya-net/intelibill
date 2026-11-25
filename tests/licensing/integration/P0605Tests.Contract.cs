using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    [Fact]
    public void ContractPinsStrictScheduleAndShopOwnerDisclosure()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/coverage-replacement-v1.json")));
        var vector = json.RootElement;
        Assert.Equal(CoverageReplacementsV1.Schedule, vector.GetProperty("scheduleAction").GetString());
        Assert.Equal(CoverageReplacementsV1.ReceiptPath, vector.GetProperty("receiptPath").GetString());
        Assert.Equal(CoverageReplacementsV1.Kind, vector.GetProperty("receiptKind").GetString());
        var request = JsonSerializer.Deserialize<ScheduleCoverageReplacementV1>(vector.GetProperty("schedule").GetRawText())!;
        Assert.Equal(P0601Tests.February29, request.ObservedEffectiveAt);
        Assert.Equal(P0601Tests.February29.AddDays(1), request.ObservedPermitDeadline);
        Assert.True(request.AcknowledgedShopSelection && request.AcknowledgedRenewal && request.AcknowledgedPermitWait);
        var response = JsonSerializer.SerializeToElement(new ReplacementOwnerV1(request.OperationKey,
            request.IncomingShopId, "South", request.ObservedEffectiveAt, "Scheduled", "Accepted", "Pending"), EntitlementSnapshotsV1.Json);
        Assert.Equal(vector.GetProperty("ownerFields").EnumerateArray().Select(v => v.GetString()).Order(),
            response.EnumerateObject().Select(e => e.Name).Order());
    }
}
