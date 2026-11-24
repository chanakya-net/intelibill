using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Refunds;

public static class RefundRequestErrors
{
    public static Error Invalid => Error.Validation(RefundRequestsV1.Invalid,
        "Provide a valid operation key, payment confirmation reference and refund reason.");
    public static Error AmountInvalid => Error.Validation(RefundRequestsV1.AmountInvalid,
        "Enter a positive whole-paise refund amount up to the verified payment amount.");
    public static Error PaymentNotFound => Error.NotFound(RefundRequestsV1.PaymentNotFound,
        "The verified payment confirmation was not found for this billing account.");
}

/// <summary>
/// Records one refund request against a verified payment confirmation under live billing membership and the account
/// lock. Authorization precedes replay; a request never approves a refund, calls a gateway or alters access.
/// </summary>
public sealed class CreateRefundRequestHandler(IBillingDelegationAccess access, IRefundRequestStore store, TimeProvider clock)
{
    public Task<ErrorOr<RefundRequestV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account,
        CreateRefundRequestV1 request, CancellationToken ct) =>
        access.ExecuteAsync<RefundRequestV1>(proof, async (_, token) =>
        {
            if (!request.HasValidIdentity() || RefundRequestsV1.NormalizeReason(request.Reason) is not { } reason)
                return RefundRequestErrors.Invalid;
            if (!request.HasValidAmount())
                return RefundRequestErrors.AmountInvalid;

            var hash = RefundRequestsV1.CreateHash(request.OrderId, request.ConfirmationId, request.AmountPaise, reason);
            if (await store.FindAsync(account, request.OperationKey, token) is { } prior)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash
                    ? prior.Request : BillingAccountErrors.PayloadMismatch;

            if (await store.PaymentAsync(account, request.OrderId, request.ConfirmationId, token) is not { } payment)
                return RefundRequestErrors.PaymentNotFound;
            if (payment.Currency != "INR" || payment.AmountPaise <= 0 || request.AmountPaise > payment.AmountPaise)
                return RefundRequestErrors.AmountInvalid;

            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            var created = new RefundRequestV1(Guid.NewGuid(), account, request.OrderId, request.ConfirmationId,
                RefundRequestsV1.Requested, request.AmountPaise, payment.AmountPaise, payment.Currency, reason, now);
            await store.SaveAsync(created, payment, proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return created;
        }, ct);
}
