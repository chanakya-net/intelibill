using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

/// <summary>
/// Withdraws a scheduled cancellation strictly before its boundary by appending the next revision. Term dates,
/// paid facts, grace and account restrictions are never written: the original grace window simply applies again.
/// Authorization and the account fence run first (in <see cref="IBillingDelegationAccess"/>); a stored receipt
/// then replays before any state or clock check, so a retry after the boundary still answers the committed outcome.
/// </summary>
public sealed class WithdrawCancellationHandler(IBillingDelegationAccess access, IScheduledSubscriptionChangeStore store,
    IEntitlementPublisher publisher, TimeProvider clock)
{
    public static Error Effective => Error.Conflict("Billing.CancellationEffective",
        "The cancellation has already taken effect and can no longer be withdrawn.");

    public Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> WithdrawAsync(VerifiedBillingDelegation proof,
        Guid account, Guid term, WithdrawSubscriptionCancellationV1 request, CancellationToken ct) =>
        access.ExecuteAsync<SubscriptionCancellationWithdrawalOutcomeV1>(proof, (_, token) =>
            AcceptAsync(proof, account, term, request, token), ct);

    public Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> RecoverAsync(VerifiedBillingDelegation proof,
        Guid account, Guid term, Guid key, CancellationToken ct) =>
        access.ExecuteAsync<SubscriptionCancellationWithdrawalOutcomeV1>(proof, async (_, token) =>
        {
            var receipt = await store.FindReceiptAsync(account, key, token);
            return receipt is { Kind: CancellationReceipt.WithdrawKind } && receipt.ActorId == proof.ActorId
                && receipt.TermId == term ? receipt.Withdrawn(account, key) : ScheduleCancellationHandler.Missing;
        }, ct);

    private async Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> AcceptAsync(VerifiedBillingDelegation proof,
        Guid account, Guid term, WithdrawSubscriptionCancellationV1 request, CancellationToken ct)
    {
        if (!request.IsValid()) return ScheduleCancellationHandler.Invalid;
        var digest = Hash(request);
        var receipt = await store.FindReceiptAsync(account, request.OperationKey, ct);
        if (receipt is not null)
            return receipt is { Kind: CancellationReceipt.WithdrawKind } && receipt.ActorId == proof.ActorId
                && receipt.TermId == term && receipt.PayloadHash == digest
                ? receipt.Withdrawn(account, request.OperationKey) : BillingAccountErrors.PayloadMismatch;

        var row = await store.ReadTermAsync(account, term, ct);
        if (row is null) return ScheduleCancellationHandler.Missing;
        var at = SubscriptionStatusProjection.EvaluationInstant(clock);
        if (row.RenewedAtEnd || Scheduled(row) is not { } saved || saved.Version != request.CurrentVersion
            || saved.ChangeId != request.ChangeId || saved.EffectiveAt != request.AcceptedEffectiveAt)
            return ScheduleCancellationHandler.Conflict;
        if (at >= saved.EffectiveAt) return Effective;

        var withdrawal = new CancellationWithdrawal(account, term, Guid.NewGuid(), saved.Version + 1, saved,
            request.OperationKey, digest, Hash(new { saved.ChangeId, saved.Version, saved.EffectiveAt, saved.ShopHash }), at);
        await store.AppendWithdrawalAsync(proof, withdrawal, ct);
        foreach (var shop in await store.ReadAssignedShopsAsync(account, term, ct))
            await publisher.PublishAsync(account, shop.ShopId, ct);
        return new SubscriptionCancellationWithdrawalOutcomeV1(1, account, term, request.OperationKey, saved.ChangeId,
            withdrawal.Revision, saved.EffectiveAt, at, saved.ShopCount);
    }

    // Only a cancellation still in force on complete paid evidence can be withdrawn.
    private static ScheduledSubscriptionChange? Scheduled(TermFactsRow row) =>
        SubscriptionStatusProjection.ActiveCancellation(row) is { } saved
        && SubscriptionStatusProjection.ToFacts(row) is { Evidence: TermEvidenceState.Complete, Basis: SubscriptionBasis.Paid }
            ? saved : null;

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
