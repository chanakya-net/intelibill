using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

public sealed class RemoveBillingManagerHandler(IBillingDelegationAccess access, IBillingMemberRemovalStore store)
{
    public Task<ErrorOr<BillingManagerRemovalV1>> HandleAsync(VerifiedBillingDelegation delegation, Guid memberId,
        RemoveBillingManagerV1 request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (memberId == Guid.Empty || request.OperationKey == Guid.Empty || request.ExpectedVersion < 1 ||
            reason is not { Length: > 0 and <= BillingInvitationsV1.MaximumRemovalReasonLength } || reason.Any(char.IsControl))
            return Task.FromResult<ErrorOr<BillingManagerRemovalV1>>(BillingMemberRemovalErrors.Invalid);

        return access.ExecuteAsync(delegation, (scope, token) => store.RemoveAsync(new(
            delegation.AccountId!.Value, scope.ActorId, memberId, request.OperationKey, request.ExpectedVersion,
            reason, delegation.RequestId, delegation.ExpiresAt), token), ct);
    }
}

public interface IBillingMemberRemovalStore
{
    Task<ErrorOr<BillingManagerRemovalV1>> RemoveAsync(BillingMemberRemoval command, CancellationToken ct);
}

public sealed record BillingMemberRemoval(Guid AccountId, Guid ActorId, Guid MemberId, Guid OperationKey,
    int ExpectedVersion, string Reason, Guid RequestId, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "[REDACTED]";
}

public static class BillingMemberRemovalErrors
{
    public static Error Invalid => Error.Validation("Billing.MemberRemovalInvalid", "Enter valid removal details and a reason.");
    public static Error Unavailable => Error.NotFound("Billing.MemberUnavailable", "This billing member is unavailable.");
    public static Error VersionConflict => Error.Conflict("Billing.MemberVersionConflict", "Billing membership changed. Reload and try again.");
    public static Error OwnerForbidden => Error.Conflict("Billing.OwnerRemovalForbidden", "The Billing Owner cannot be removed.");
}
