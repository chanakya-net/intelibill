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
            if (!await status.MayAccessOrderAsync(account, order, proof.ActorId, token))
                return BillingDelegationErrors.Denied;
            if (!request.IsValid())
                return CheckoutPaymentErrors.Invalid;
            if (await store.FindAttemptAsync(account, attemptId, false, token) is not { } attempt || attempt.OrderId != order)
                return CheckoutPaymentErrors.Missing;
            await store.UseAttemptAsync(attemptId, token);
            var verified = await store.HasVerifiedPaymentAsync(attemptId, token);
            // A browser claim makes the attempt uncertain. A proof-free recheck does not: reconciliation records uncertainty only when it finds payment evidence, so an unpaid order stays payable.
            if (!verified && (request.LauncherOutcome is not null || request.LauncherProof is not null))
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
        else
        {
            var outcome = await reconcile.HandleAsync(stored, ct, request.LauncherProof,
                request.LauncherOutcome is not null || request.LauncherProof is not null,
                request.LauncherOutcome == "Success");
            if (outcome.IsError)
                return outcome.Errors;
        }
        return await store.InAttemptScopeAsync(account, attemptId, false, token => status.ReadAsync(account, order, token), ct);
    }
}
