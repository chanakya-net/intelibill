using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

/// <summary>Owner preview of a prorated capacity increase, relayed to licensing under a signed delegation.</summary>
public interface IMerchantCapacityChangeQuotes
{
    Task<ErrorOr<CheckoutQuoteV1>> ConvertCapacityQuoteAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid quote, ConvertCapacityQuoteV1 request, CancellationToken ct);
    Task<ErrorOr<CapacityChangeQuoteV1>> CreateCapacityQuoteAsync(MerchantBillingCaller caller, Guid account, Guid term,
        CreateCapacityChangeQuoteV1 request, CancellationToken ct);
    Task<ErrorOr<CapacityChangeShopPageV1>> ReadCapacityQuoteShopsAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid quote, Guid? after, CancellationToken ct);
}
