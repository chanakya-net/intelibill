using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed class ReadPaymentConfirmationHandler(IBillingDelegationAccess access, IManualPaymentStore admins,
    ICheckoutOrderStore orders, IPaymentConfirmationStore confirmations)
{
    public Task<ErrorOr<PaymentConfirmationV1[]>> ListAsync(VerifiedBillingDelegation proof, Guid account, Guid order, CancellationToken ct) =>
        access.ExecuteAsync(proof, (_, token) => ListAsync(account, order, token), ct);
    public Task<ErrorOr<byte[]>> DocumentAsync(VerifiedBillingDelegation proof, Guid account, Guid order, Guid id, CancellationToken ct) =>
        access.ExecuteAsync(proof, (_, token) => DocumentAsync(account, order, id, token), ct);
    public Task<ErrorOr<PaymentConfirmationV1[]>> ListAsync(AdminSessionCaller caller, Guid account, Guid order, CancellationToken ct) =>
        admins.InManualScopeAsync<PaymentConfirmationV1[]>(caller, account, order, false, false,
            (_, token) => ListAsync(account, order, token), ct);
    public Task<ErrorOr<byte[]>> DocumentAsync(AdminSessionCaller caller, Guid account, Guid order, Guid id, CancellationToken ct) =>
        admins.InManualScopeAsync<byte[]>(caller, account, order, false, false,
            (_, token) => DocumentAsync(account, order, id, token), ct);

    private async Task<ErrorOr<PaymentConfirmationV1[]>> ListAsync(Guid account, Guid order, CancellationToken ct) =>
        await orders.FindAsync(account, order, false, ct) is null ? Missing :
            await confirmations.ListConfirmationsAsync(account, order, ct);
    private async Task<ErrorOr<byte[]>> DocumentAsync(Guid account, Guid order, Guid id, CancellationToken ct) =>
        await confirmations.FindConfirmationAsync(account, order, id, ct) is { } snapshot
            ? PaymentConfirmationBuilder.Render(snapshot) : Missing;
    private static Error Missing => Error.NotFound(PaymentConfirmationsV1.NotFound, "The payment confirmation was not found.");
}
