using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public sealed record MerchantBillingDocument(byte[] Bytes, string FileName);
public interface IMerchantPaymentConfirmations
{
    Task<ErrorOr<PaymentConfirmationV1[]>> ListConfirmationsAsync(MerchantBillingCaller caller, Guid account, Guid order, Guid? after, CancellationToken ct);
    Task<ErrorOr<MerchantBillingDocument>> DownloadConfirmationAsync(MerchantBillingCaller caller, Guid account, Guid order, Guid id, CancellationToken ct);
}
