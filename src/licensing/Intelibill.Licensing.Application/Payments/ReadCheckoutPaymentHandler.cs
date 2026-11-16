using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed class ReadCheckoutPaymentHandler(IBillingDelegationAccess access, CheckoutPaymentStatusReader status)
{
    public Task<ErrorOr<CheckoutPaymentV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        CancellationToken ct) =>
        access.ExecuteAsync(proof, (_, token) => status.ReadAsync(account, order, token), ct);
}
