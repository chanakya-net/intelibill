using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>Local simulator only: records a full capture on the simulated provider. It grants nothing by itself.</summary>
public sealed class SimulateCheckoutCaptureHandler(IBillingDelegationAccess access, IPaymentStore store,
    IPaymentGatewayResolver gateways, CheckoutPaymentStatusReader status, TimeProvider clock)
{
    public async Task<ErrorOr<CheckoutPaymentV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        Guid attemptId, SimulateCheckoutCaptureV1 request, CancellationToken ct)
    {
        var found = await access.ExecuteAsync<StoredPaymentAttempt>(proof, async (_, token) =>
        {
            if (!request.IsValid())
                return CheckoutPaymentErrors.Invalid;
            if (await store.FindAttemptAsync(account, attemptId, false, token) is not { ProviderOrderReference: not null } attempt ||
                attempt.OrderId != order)
                return CheckoutPaymentErrors.Missing;
            if (request.Outcome != "Captured" && !await store.HasVerifiedPaymentAsync(attemptId, token))
            {
                await store.UseAttemptAsync(attemptId, token);
                await store.AppendEventAsync(attempt, PaymentEventKinds.VerificationRequested, proof.ActorId,
                    BillingAccountRules.StorageInstant(clock.GetUtcNow()), token);
            }
            return attempt;
        }, ct);
        if (found.IsError)
            return found.Errors;
        if (gateways.ForPin(found.Value.Pin) is not IPaymentSimulator simulator) return CheckoutPaymentErrors.Unavailable;
        await simulator.SimulateAsync(found.Value.ProviderOrderReference!, request.Outcome, ct);
        return await store.InAttemptScopeAsync(account, attemptId, false, token => status.ReadAsync(account, order, token), ct);
    }
}
