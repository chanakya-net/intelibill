using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageTransferStore
{
    public async Task<StoredTransferApproval?> LookupAsync(Guid source, Guid key, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<DecisionRow>($"""
            SELECT a.admin_id, a.hash, a.transfer_id, a.operation_key, a.cutover_at, a.paid_through_at,
                CASE WHEN e.state='PendingActivation' THEN public.transfer_activation_state(a.transfer_id)
                    ELSE coalesce(e.state,'Preparing') END AS state, e.reason
            FROM public.coverage_transfer_approvals a
            LEFT JOIN LATERAL (SELECT state,reason FROM public.coverage_transfer_progress
                WHERE transfer_id = a.transfer_id ORDER BY sequence DESC LIMIT 1) e ON true
            WHERE a.source_account_id = {source} AND a.operation_key = {key}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.AdminId, row.Hash, new(row.TransferId, row.OperationKey, row.State,
            row.Reason, row.CutoverAt, row.PaidThroughAt));
    }

    public async Task<ErrorOr<StoredTransferApproval>> DecideAsync(AdminSessionCaller caller, TransferReview p,
        CoverageTransferReviewObservation observation, TransferApprovalRequest request, string hash, DateTimeOffset mfa,
        CancellationToken ct)
    {
        var locked = await EvaluateInTransactionAsync(p.TransferId, p.ProposalRevision, p.TermsDigest, observation.MerchantProof, ct);
        if (await LookupAsync(p.SourceAccountId, request.OperationKey, ct) is { } prior)
            return ApproveCoverageTransferHandler.Replay(prior, caller, p.TransferId, hash);
        if (locked.Observation is not { } current || !current.Consents.SequenceEqual(observation.Consents))
            return CoverageTransferErrors.Changed;
        var fresh = await ReviewAsync(p.SourceAccountId, p.TransferId, ct);
        if (fresh.IsError || fresh.Value.Blocker is not null || fresh.Value.ReviewDigest != request.ExpectedReviewDigest ||
            fresh.Value.MemberAllowance is not > 0) return CoverageTransferErrors.Changed;
        var owner = current.Consents.Single(c => c.Purpose == CoverageTransfersV1.ShopOwner);
        var command = new TransferCommandV1(1, Guid.NewGuid(), CoverageTransferApprovalsV1.Prepare, p.TransferId,
            p.SourceAccountId, p.DestinationAccountId, p.ShopId, p.SourceAffiliationId, p.SourceTermId, p.ProposalRevision,
            p.TermsDigest, owner.EventId, owner.ActorId, owner.MemberId, owner.AuthorityRevision, p.Treatment, p.CutoverAt,
            p.SourcePaidThroughAt, fresh.Value.MemberAllowance.Value, observation.MerchantPermitDigest!, observation.MerchantProof!.EvaluatedAt);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(command, EntitlementSnapshotsV1.Json);
        var bindings = current.Consents.Select(c => c.EventId).ToArray();
        var count = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_approvals (transfer_id, source_account_id, destination_account_id,
                shop_id, affiliation_id, source_term_id, admin_id, session_id, mfa_verified_at, operation_key, hash,
                reason, review_digest, consents, cutover_at, paid_through_at, prepare_event, decided_at)
            VALUES ({p.TransferId},{p.SourceAccountId},{p.DestinationAccountId},{p.ShopId},{p.SourceAffiliationId},
                {p.SourceTermId},{caller.AdminId},{caller.SessionId},{mfa},{request.OperationKey},{hash},{request.Reason},
                {request.ExpectedReviewDigest},{bindings},{p.CutoverAt},{p.SourcePaidThroughAt},{command.EventId},{current.ObservedAt})
            ON CONFLICT DO NOTHING
            """, ct);
        if (count != 1) return CoverageTransferErrors.Conflict;
        await new LeasedHttpOutbox(db).AppendAsync(command.EventId, p.SourceAccountId, p.ShopId,
            command.Phase, bytes, SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(deliveryClock), ct);
        return new StoredTransferApproval(caller.AdminId, hash, new(p.TransferId, request.OperationKey, "Preparing", null,
            p.CutoverAt, p.SourcePaidThroughAt), true);
    }

    private sealed record DecisionRow(Guid AdminId, string Hash, Guid TransferId, Guid OperationKey,
        DateTimeOffset CutoverAt, DateTimeOffset PaidThroughAt, string State, string? Reason);
}
