using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public sealed record MerchantCheckoutQuote(CheckoutQuoteV1 Quote, bool Created);

public interface IMerchantCheckoutOrders
{
    Task<ErrorOr<MerchantCheckoutQuote>> CreateQuoteAsync(MerchantBillingCaller caller, Guid account,
        CreateCheckoutQuoteV1 request, CancellationToken ct);
    Task<ErrorOr<CheckoutQuoteV1>> AcceptQuoteAsync(MerchantBillingCaller caller, Guid account, Guid order,
        AcceptCheckoutQuoteV1 request, CancellationToken ct);
    Task<ErrorOr<CheckoutQuoteV1>> ReadQuoteAsync(MerchantBillingCaller caller, Guid account, Guid order, CancellationToken ct);
    Task<ErrorOr<CheckoutQuoteV1>> RecoverQuoteAsync(MerchantBillingCaller caller, Guid account, string kind, Guid key,
        CancellationToken ct);
    Task<ErrorOr<CheckoutShopPageV1>> ReadCheckoutShopsAsync(MerchantBillingCaller caller, Guid account,
        Guid? order, Guid? after, CancellationToken ct);
}
