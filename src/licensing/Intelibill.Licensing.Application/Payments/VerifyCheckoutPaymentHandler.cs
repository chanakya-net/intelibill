using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>
/// Records the browser's report as a claim, then asks the pinned gateway, server to server and with no transaction
/// open, whether the full amount was captured. Only a captured observation reaches fulfilment.
/// </summary>
public sealed class VerifyCheckoutPaymentHandler(IBillingDelegationAccess access, IPaymentStore store,
    ReconcilePaymentHandler reconcile, FulfilPaidOrderHandler fulfil, CheckoutPaymentStatusReader status, TimeProvider clock)
{
    public async Task<ErrorOr<CheckoutPaymentV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        Guid attemptId, VerifyCheckoutPaymentV1 request, CancellationToken ct)
    {
        var claimed = await access.ExecuteAsync<(StoredPaymentAttempt Attempt, bool Verified)>(proof, async (_, token) =>
        {
            if (!request.IsValid())
                return CheckoutPaymentErrors.Invalid;
            if (await store.FindAttemptAsync(account, attemptId, false, token) is not { } attempt || attempt.OrderId != order)
                return CheckoutPaymentErrors.Missing;
            await store.UseAttemptAsync(attemptId, token);
            var verified = await store.HasVerifiedPaymentAsync(attemptId, token);
            if (!verified)
                await store.AppendEventAsync(attempt, PaymentEventKinds.VerificationRequested, proof.ActorId,
                    BillingAccountRules.StorageInstant(clock.GetUtcNow()), token);
            return (attempt, verified);
        }, ct);
        if (claimed.IsError)
            return claimed.Errors;

        var (stored, alreadyVerified) = claimed.Value;
        if (alreadyVerified)
        {
            if (await fulfil.RetryAsync(stored, ct) is { IsError: true } retry)
                return retry.Errors;
        }
        else if (await reconcile.HandleAsync(stored, ct) is { IsError: true } verification)
            return verification.Errors;
        return await store.InAttemptScopeAsync(account, attemptId, false, token => status.ReadAsync(account, order, token), ct);
    }
}
