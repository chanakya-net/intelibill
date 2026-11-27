using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferApprovalRequest(Guid OperationKey, string Reason, string ExpectedReviewDigest,
    string Treatment, DateTimeOffset CutoverAt, DateTimeOffset PaidThroughAt, bool AcknowledgedAllocation,
    bool AcknowledgedPermitsAndUnknownQueues, bool AcknowledgedHistoricalBillsStay);

public sealed record TransferApproval(Guid TransferId, Guid OperationKey, string State, string? OutcomeReason,
    DateTimeOffset CutoverAt, DateTimeOffset PaidThroughAt);
public sealed record StoredTransferApproval(Guid Actor, string Hash, TransferApproval Approval, bool Created = false);

public sealed partial class ApproveCoverageTransferHandler
{
    public async Task<ErrorOr<StoredTransferApproval>> ApproveAsync(AdminSessionCaller caller, Guid source, Guid transfer,
        TransferApprovalRequest? request, CancellationToken ct)
    {
        if (request is not { AcknowledgedAllocation: true, AcknowledgedHistoricalBillsStay: true,
                AcknowledgedPermitsAndUnknownQueues: true } || request.OperationKey == Guid.Empty ||
            CoverageTransfersV1.NormalizeReason(request.Reason) is not { } reason)
            return CoverageTransferErrors.Invalid;
        request = request with { Reason = reason };
        var hash = CheckoutOrdersV1.Digest("coverage-transfer-approval/1", source, transfer, request);
        var prior = await InScopeAsync<StoredTransferApproval?>(caller, source, true,
            async token => await store.LookupAsync(source, request.OperationKey, token), ct);
        if (prior.IsError) return prior.Errors;
        if (prior.Value is { } saved) return Replay(saved, caller, transfer, hash);
        var preview = await PreviewAsync(caller, source, transfer, ct);
        if (preview.IsError) return preview.Errors;
        var p = preview.Value;
        if (p.Blocker is not null || p.ReviewDigest != request.ExpectedReviewDigest || p.Treatment != request.Treatment ||
            p.CutoverAt != request.CutoverAt || p.SourcePaidThroughAt != request.PaidThroughAt)
            return CoverageTransferErrors.Changed;
        var check = await authority.CheckForReviewAsync(transfer, p.ProposalRevision, p.TermsDigest, ct);
        if (check.Outcome == CoverageTransferReviewOutcome.Unavailable)
            return Error.Failure("Coverage.TransferAuthorityUnavailable", "Shop authority could not be confirmed. Retry this decision.");
        if (check.Observation is not { MerchantProof: not null, ProofDeadline: { } deadline } observation)
            return CoverageTransferErrors.Changed;
        return await InScopeAsync<StoredTransferApproval>(caller, source, true, async token =>
        {
            var auth = await manager.AuthorizeAsync(caller, true, token);
            if (auth.Error is { } denied) return denied;
            var decision = await store.DecideAsync(caller, p, observation, request, hash, auth.MfaVerifiedAt!.Value, token);
            if (sessionClock.GetUtcNow() >= deadline) return CoverageTransferErrors.Changed;
            return decision;
        }, ct);
    }

    public Task<ErrorOr<TransferApproval>> RecoverAsync(AdminSessionCaller caller, Guid source, Guid transfer, Guid key,
        CancellationToken ct) => InScopeAsync<TransferApproval>(caller, source, true, async token =>
        await store.LookupAsync(source, key, token) is { } prior && prior.Actor == caller.AdminId &&
            prior.Approval.TransferId == transfer ? prior.Approval : CoverageTransferErrors.NotFound, ct);

    public static ErrorOr<StoredTransferApproval> Replay(StoredTransferApproval prior, AdminSessionCaller caller,
        Guid transfer, string hash) => prior.Actor == caller.AdminId && prior.Hash == hash && prior.Approval.TransferId == transfer
        ? prior : BillingAccountErrors.PayloadMismatch;
}
