using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class BillingOwnershipTransferStore
{
    public async Task<ErrorOr<BillingOwnershipOutcomeV1>> AcceptAsync(IBillingDelegationScope scope,
        VerifiedBillingDelegation proof, Guid transfer, AcceptBillingOwnershipTransferV1 request, CancellationToken ct)
    {
        var proposal = await db.Database.SqlQuery<ProposalScope>($"""
            SELECT account_id, expires_at FROM public.billing_ownership_proposals
            WHERE id = {transfer} AND recipient_actor_id = {scope.ActorId}
            """).SingleOrDefaultAsync(ct);
        if (proposal is null)
            return BillingOwnershipTransferErrors.Missing;

        await scope.UseAccountAsync(proposal.AccountId, ct);
        await BillingAccountLock.TakeAsync(db, proposal.AccountId, ct);
        if (Now >= proof.ExpiresAt)
            return BillingDelegationErrors.Invalid;

        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { transfer, request }));
        if (await FindReceiptAsync(request.OperationKey, ct) is { } receipt)
            return CryptographicOperations.FixedTimeEquals(receipt.RequestHash, hash)
                ? receipt.ToOutcome() : BillingAccountErrors.PayloadMismatch;
        if (request.ExpectedVersion != 1)
            return BillingOwnershipTransferErrors.Stale;

        return await ChangeOwnerAsync(proof, proposal, transfer, request, hash, ct);
    }

    private async Task<ErrorOr<BillingOwnershipOutcomeV1>> ChangeOwnerAsync(VerifiedBillingDelegation proof,
        ProposalScope proposal, Guid transfer, AcceptBillingOwnershipTransferV1 request, byte[] hash, CancellationToken ct)
    {
        var now = Now;
        var committed = await db.Database.SqlQuery<bool>($"""
            SELECT public.accept_billing_ownership_transfer({transfer}, {Guid.CreateVersion7(now)}, {request.OperationKey},
                {proof.RequestId}, {hash}, {request.ResponsibilityPolicyVersion}, {now}) AS "Value"
            """).SingleAsync(ct);

        if (Now >= proof.ExpiresAt)
            return BillingDelegationErrors.Invalid;
        return committed && Now < proposal.ExpiresAt
            ? new BillingOwnershipOutcomeV1(request.OperationKey, proposal.AccountId, transfer, now, MerchantBillingDelegationV1.OwnerRole)
            : BillingOwnershipTransferErrors.Stale;
    }

    public async Task<ErrorOr<BillingOwnershipOutcomeV1>> RecoverAcceptAsync(Guid key, CancellationToken ct) =>
        await FindReceiptAsync(key, ct) is { } receipt ? receipt.ToOutcome() : BillingOwnershipTransferErrors.Missing;

    private Task<AcceptanceReceipt?> FindReceiptAsync(Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<AcceptanceReceipt>($"""
            SELECT operation_key, account_id, transfer_id, effective_at, request_hash
            FROM public.billing_ownership_acceptances WHERE operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    private sealed record ProposalScope(Guid AccountId, DateTimeOffset ExpiresAt);

    private sealed record AcceptanceReceipt(Guid OperationKey, Guid AccountId, Guid TransferId, DateTimeOffset EffectiveAt,
        byte[] RequestHash)
    {
        internal BillingOwnershipOutcomeV1 ToOutcome() =>
            new(OperationKey, AccountId, TransferId, EffectiveAt, MerchantBillingDelegationV1.OwnerRole);
    }
}
