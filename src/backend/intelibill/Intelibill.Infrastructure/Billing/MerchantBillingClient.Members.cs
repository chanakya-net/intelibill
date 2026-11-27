using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public Task<ErrorOr<BillingManagerRemovalV1>> RemoveManagerAsync(MerchantBillingCaller caller, Guid accountId,
        Guid memberId, RemoveBillingManagerV1 request, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (memberId == Guid.Empty || request.OperationKey == Guid.Empty || request.ExpectedVersion < 1 ||
            reason is not { Length: > 0 and <= BillingInvitationsV1.MaximumRemovalReasonLength } || reason.Any(char.IsControl))
            return Task.FromResult<ErrorOr<BillingManagerRemovalV1>>(InvalidRemoval());

        var body = JsonSerializer.SerializeToUtf8Bytes(request with { Reason = reason }, Json);
        return ReadInvitationAsync<BillingManagerRemovalV1>(caller,
            new(BillingInvitationsV1.RemoveMember, HttpMethod.Post, BillingInvitationsV1.MemberRemoval(accountId, memberId),
                accountId, body),
            result => result.AccountId == accountId && result.MemberId == memberId &&
                result.OperationKey == request.OperationKey && result.Revision == (long)request.ExpectedVersion + 1 &&
                result.Status == "Removed" && result.RemovedAt != default, cancellationToken);
    }

    private Error MapRemovalFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, "Billing.MemberRemovalInvalid") => InvalidRemoval(),
        (HttpStatusCode.NotFound, "Billing.MemberUnavailable") =>
            Error.NotFound(code, "This billing member is unavailable."),
        (HttpStatusCode.Conflict, "Billing.MemberVersionConflict") =>
            Error.Conflict(code, "Billing membership changed. Reload and try again."),
        (HttpStatusCode.Conflict, "Billing.OwnerRemovalForbidden") =>
            Error.Conflict(code, "The Billing Owner cannot be removed."),
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        _ => Unavailable("UpstreamStatus"),
    };

    private static Error InvalidRemoval() => Error.Validation("Billing.MemberRemovalInvalid",
        "Enter valid removal details and a reason.");
}
