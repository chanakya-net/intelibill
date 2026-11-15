using ErrorOr;
using Intelibill.Licensing.Application.Billing;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed class UnavailableBillingAccountDelegationAuthority : IBillingAccountDelegationAuthority
{
    public Task<ErrorOr<Success>> AuthorizeAsync(Guid accountId, Guid actorId, string action, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Success>>(BillingDelegationErrors.MembershipUnavailable);
}
