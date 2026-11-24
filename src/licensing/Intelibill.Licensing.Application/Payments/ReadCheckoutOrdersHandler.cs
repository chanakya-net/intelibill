using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed class ReadCheckoutOrdersHandler(IBillingDelegationAccess access, ICheckoutOrderStore store)
{
    public Task<ErrorOr<CheckoutQuoteV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account, Guid id,
        string? operation, CancellationToken ct) =>
        access.ExecuteAsync<CheckoutQuoteV1>(proof, async (_, token) =>
        {
            if (operation is not (null or "create" or "accept"))
                return CheckoutOrderErrors.Missing;
            var acceptance = operation == "accept" ? await store.FindAcceptanceAsync(account, id, token) : null;
            if (operation == "accept" && acceptance?.ActorId != proof.ActorId)
                return CheckoutOrderErrors.Missing;
            var retained = await store.FindAsync(account, acceptance?.OrderId ?? id, operation == "create", token);
            if (retained?.Quote.Increase is not null && !await store.IsIncreaseOwnerAsync(account, retained.Quote.OrderId, proof.ActorId, token))
                return BillingDelegationErrors.Denied;
            return retained is not null && (operation != "create" || retained.ActorId == proof.ActorId)
                ? retained.Quote : CheckoutOrderErrors.Missing;
        }, ct);

    public Task<ErrorOr<CheckoutShopPageV1>> PageAsync(VerifiedBillingDelegation proof, Guid account, Guid? order,
        Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<CheckoutShopPageV1>(proof, async (_, token) =>
        {
            if (after == Guid.Empty)
                return CheckoutOrderErrors.Invalid;
            if (order is { } id)
            {
                var retained = await store.FindAsync(account, id, false, token);
                if (retained is null) return CheckoutOrderErrors.Missing;
                if (retained.Quote.Increase is not null && !await store.IsIncreaseOwnerAsync(account, id, proof.ActorId, token))
                    return BillingDelegationErrors.Denied;
            }
            return await store.PageAsync(account, order, after, token);
        }, ct);
}
