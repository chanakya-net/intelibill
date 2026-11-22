using AuthErrors = Intelibill.Application.Common.Errors.Errors.Auth;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Common.Interfaces;
using ErrorOr;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Application.Common.Licensing;

public static class LicenceGate
{
    public static async Task<Error?> ReadTradingDenialAsync(this IShopLicenceFence fence, Guid shopId, CancellationToken ct)
    {
        var result = await fence.EnterAsync(shopId, LicenceAction.NewTrading, ct);
        if (result.IsError)
            return result.FirstError;

        return result.Value.Decision.EnforcedDenial();
    }

    public static async Task<ErrorOr<ShopLicenceLease>> EnterForSessionAsync(this IShopLicenceFence fence,
        ILiveSessionAuthority authority, SessionClaims? session, Guid userId, Guid shopId, LicenceAction action,
        CancellationToken ct)
    {
        if (await authority.SessionDenialAsync(session, userId, shopId, ct) is { } admissionDenial)
            return admissionDenial;

        var lease = await fence.EnterAsync(shopId, action, ct);
        if (await authority.SessionDenialAsync(session, userId, shopId, ct) is { } writeDenial)
            return writeDenial;
        return lease;
    }

    public static async Task<Error?> SessionDenialAsync(this ILiveSessionAuthority authority,
        SessionClaims? session, Guid userId, Guid shopId, CancellationToken ct)
    {
        if (session is null) return null; // Existing in-process callers supply independently checked actor authority.
        return session.UserId == userId && session.ActiveShopId == shopId && await authority.IsValidAsync(session, ct)
            ? null
            : AuthErrors.SessionInvalidated;
    }

    public static Error? ExistingSourceDenial(this LicenceDecision decision, DateTimeOffset sourceAt) =>
        decision.Enforced && decision.SourceCompletionCutoff is { } cutoff && sourceAt >= cutoff
            ? decision.OperatingRefusal ?? LicenceErrors.VerificationUnavailable
            : decision.EnforcedDenial();

    public static Error? EnforcedDenial(this LicenceDecision decision) =>
        decision is { Enforced: true, Outcome: LicenceOutcome.Blocked }
            ? decision.Denial ?? LicenceErrors.VerificationUnavailable
            : null;
}
