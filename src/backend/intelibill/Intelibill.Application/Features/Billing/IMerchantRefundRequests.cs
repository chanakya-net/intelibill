using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

/// <summary>Account-scoped refund requests relayed to licensing under a signed merchant billing delegation.</summary>
public interface IMerchantRefundRequests
{
    Task<ErrorOr<RefundRequestV1>> CreateRefundRequestAsync(MerchantBillingCaller caller, Guid account,
        CreateRefundRequestV1 request, CancellationToken ct);
    Task<ErrorOr<RefundRequestPageV1>> ListRefundRequestsAsync(MerchantBillingCaller caller, Guid account,
        Guid? after, CancellationToken ct);
}
