using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantBillingDocuments
{
    Task<ErrorOr<BillingDocumentListV1>> ListDocumentsAsync(MerchantBillingCaller caller, Guid account, Guid order,
        CancellationToken ct, int? beforeVersion = null);
    Task<ErrorOr<MerchantBillingDocument>> DownloadDocumentAsync(MerchantBillingCaller caller, Guid account, Guid order, Guid version, CancellationToken ct);
}
