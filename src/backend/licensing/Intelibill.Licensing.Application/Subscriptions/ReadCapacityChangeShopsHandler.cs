using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

/// <summary>One page of the shops a quote froze, bound to its account, term and quote; never derived from the caller.</summary>
public sealed class ReadCapacityChangeShopsHandler(IBillingDelegationAccess access, ICapacityChangeQuoteStore store)
{
    public Task<ErrorOr<CapacityChangeShopPageV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        Guid quote, Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<CapacityChangeShopPageV1>(proof, async (_, token) =>
            await store.PageAsync(account, term, quote, after, token) is { } page
                ? page : CapacityChangeQuoteErrors.Missing, ct);
}
