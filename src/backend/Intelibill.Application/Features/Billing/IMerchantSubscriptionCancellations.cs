using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantSubscriptionCancellations
{
    Task<ErrorOr<SubscriptionCancellationDetailV1>> ReadCancellationAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid? after, CancellationToken ct);
    Task<ErrorOr<SubscriptionCancellationOutcomeV1>> ScheduleCancellationAsync(MerchantBillingCaller caller, Guid account,
        Guid term, ScheduleSubscriptionCancellationV1 request, CancellationToken ct);
    Task<ErrorOr<SubscriptionCancellationOutcomeV1>> RecoverCancellationAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid operation, CancellationToken ct);
    Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> WithdrawCancellationAsync(MerchantBillingCaller caller,
        Guid account, Guid term, WithdrawSubscriptionCancellationV1 request, CancellationToken ct);
    Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> RecoverCancellationWithdrawalAsync(MerchantBillingCaller caller,
        Guid account, Guid term, Guid operation, CancellationToken ct);
}
