using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>Local simulator only: records a full capture on the simulated provider. It grants nothing by itself.</summary>
public sealed class SimulateCheckoutCaptureHandler(IBillingDelegationAccess access, IPaymentStore store,
    IPaymentSimulator simulator, CheckoutPaymentStatusReader status)
{
    public async Task<ErrorOr<CheckoutPaymentV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        Guid attemptId, SimulateCheckoutCaptureV1 request, CancellationToken ct)
    {
        var found = await access.ExecuteAsync<StoredPaymentAttempt>(proof, async (_, token) =>
            !request.IsValid() ? CheckoutPaymentErrors.Invalid
            : await store.FindAttemptAsync(account, attemptId, false, token) is { ProviderOrderReference: not null } attempt &&
              attempt.OrderId == order ? attempt : CheckoutPaymentErrors.Missing, ct);
        if (found.IsError)
            return found.Errors;
        await simulator.CaptureAsync(found.Value.ProviderOrderReference!, ct);
        return await store.InAttemptScopeAsync(account, attemptId, false, token => status.ReadAsync(account, order, token), ct);
    }
}
