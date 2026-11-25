using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public void ContractPinsStrictRequestPathsAndNarrowOwnerDisclosure()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/coverage-replacement-exception-v1.json")));
        var v = json.RootElement;
        Assert.Equal(CoverageReplacementExceptionsV1.PolicyVersion, v.GetProperty("policyVersion").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.Read, v.GetProperty("readAction").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.Request, v.GetProperty("requestAction").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.Recover, v.GetProperty("recoverAction").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.OwnerRead, v.GetProperty("ownerReadAction").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.ConsentReceiptPath, v.GetProperty("consentReceiptPath").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.DecisionReceiptPath, v.GetProperty("decisionReceiptPath").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.PreparePath, v.GetProperty("preparePath").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.ConsumePath, v.GetProperty("consumePath").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.AbortPath, v.GetProperty("abortPath").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.ConsentKind, v.GetProperty("consentKind").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.ReceiptKind, v.GetProperty("receiptKind").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.PrepareKind, v.GetProperty("prepareKind").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.ConsumeKind, v.GetProperty("consumeKind").GetString());
        Assert.Equal(CoverageReplacementExceptionsV1.AbortKind, v.GetProperty("abortKind").GetString());

        var req = JsonSerializer.Deserialize<RequestCoverageReplacementExceptionV1>(v.GetProperty("request").GetRawText())!;
        Assert.Equal(EarlyStart, req.RequestedEarlyStartAt);
        Assert.Equal(EarlyStart.AddHours(4), req.ProposedPaidCutoverAt);
        Assert.Equal(CoverageReplacementExceptionsV1.CourtesyBridgeDisposition, req.OverlapDisposition);
        Assert.True(req.AcknowledgedShopSelection && req.AcknowledgedRemainingAccessAllocation
            && req.AcknowledgedExistingPermits && req.AcknowledgedUnknownDisconnectedQueues);

        // Unmapped properties must fail strict deserialization.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequestCoverageReplacementExceptionV1>(
            v.GetProperty("request").GetRawText().TrimEnd('}') + ",\"extra\":true}"));

        var digest = CoverageReplacementExceptionsV1.TermsDigest(
            Guid.NewGuid(), Guid.NewGuid(), 1, req.OutgoingShopId, Guid.NewGuid(), req.IncomingShopId, Guid.NewGuid(),
            req.ObservedCapacity, req.ObservedMemberAllowance, req.RequestedEarlyStartAt, req.ProposedPaidCutoverAt,
            req.ObservedNormalRenewalAt, req.OverlapDisposition, req.ObservedPermitDeadline);
        var owner = JsonSerializer.SerializeToElement(new CoverageReplacementExceptionOwnerV1(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, digest, CoverageReplacementExceptionsV1.OutgoingLeg,
            req.OutgoingShopId, "North", "South", req.ObservedNormalRenewalAt, req.RequestedEarlyStartAt,
            req.ProposedPaidCutoverAt, req.OverlapDisposition, req.ObservedMemberAllowance, req.ObservedPermitDeadline,
            true, "AwaitingConsents", "AwaitingConsent", "Pending", EarlyStart.AddDays(7)), EntitlementSnapshotsV1.Json);
        Assert.Equal(v.GetProperty("ownerFields").EnumerateArray().Select(x => x.GetString()).Order(),
            owner.EnumerateObject().Select(p => p.Name).Order());
    }
}
