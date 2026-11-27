using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

/// <summary>Decides whether an Owner or Manager may correct stock now: enters the GeneralStock licence and re-checks session and role.</summary>
public sealed class StockCorrectionAccess(
    IShopRepository shopRepository,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority sessions)
{
    internal async Task<Error?> CheckAuthorityAsync(Guid actorUserId, Guid activeShopId, SessionClaims? commandSession, CancellationToken ct)
    {
        if (commandSession is { } session &&
            (session.UserId != actorUserId || session.ActiveShopId != activeShopId ||
             !await sessions.IsValidAsync(session, ct)))
            return Errors.Auth.SessionInvalidated;

        return await shopRepository.GetActiveRoleAsync(actorUserId, activeShopId, ct) switch
        {
            ShopRole.Owner or ShopRole.Manager => null,
            null => Errors.Shop.MembershipNotFound,
            _ => Errors.Inventory.UserIsNotOwnerOrManager,
        };
    }

    internal async Task<ErrorOr<ShopLicenceLease>> EnterLicenceAsync(
        Guid actorUserId,
        Guid activeShopId,
        SessionClaims? session,
        Action<ShopLicenceLease>? retainLease,
        CancellationToken cancellationToken)
    {
        var licence = await licenceFence.EnterAsync(activeShopId, LicenceAction.GeneralStock, cancellationToken);
        if (licence.IsError)
            return licence.Errors;
        retainLease?.Invoke(licence.Value);
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } denied)
            return denied;
        if (await CheckAuthorityAsync(actorUserId, activeShopId, session, cancellationToken) is { } admissionDenied)
            return admissionDenied;

        return licence;
    }
}
