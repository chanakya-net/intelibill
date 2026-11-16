using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public sealed record MerchantCheckoutPayment(CheckoutPaymentV1 Payment, bool Created);

/// <summary>Relays the provider-neutral checkout payment actions to licensing; provider facts never reach the merchant.</summary>
public interface IMerchantCheckoutPayments
{
    Task<ErrorOr<CheckoutPaymentV1>> ReadPaymentAsync(MerchantBillingCaller caller, Guid account, Guid order, CancellationToken ct);
    Task<ErrorOr<MerchantCheckoutPayment>> StartPaymentAsync(MerchantBillingCaller caller, Guid account, Guid order,
        StartCheckoutPaymentV1 request, CancellationToken ct);
    Task<ErrorOr<CheckoutPaymentV1>> VerifyPaymentAsync(MerchantBillingCaller caller, Guid account, Guid order, Guid attempt,
        VerifyCheckoutPaymentV1 request, CancellationToken ct);
    Task<ErrorOr<CheckoutPaymentV1>> SimulateCaptureAsync(MerchantBillingCaller caller, Guid account, Guid order, Guid attempt,
        SimulateCheckoutCaptureV1 request, CancellationToken ct);
}
