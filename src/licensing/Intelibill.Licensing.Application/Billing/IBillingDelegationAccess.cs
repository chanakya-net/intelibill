using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Billing;

public sealed record VerifiedBillingDelegation(
    Guid ActorId, Guid AccountId, string Action, Guid RequestId, Guid ReplayId,
    string KeyId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
    string Method, string Path, string BodySha256);

public interface IBillingDelegationAccess
{
    Task<ErrorOr<BillingAccountAccessV1>> AuthorizeAsync(VerifiedBillingDelegation delegation, CancellationToken cancellationToken);
}
