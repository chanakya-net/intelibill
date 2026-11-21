using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

public sealed class StockIntakeAccess(
    IShopRepository shops,
    ILiveSessionAuthority sessions,
    IShopLicenceFence fence)
{
    public async Task<Error?> CheckAuthorityAsync(Guid actor, Guid shop, SessionClaims? session, CancellationToken ct, Error? roleRefusal = null)
    {
        if (session is null || session.UserId != actor || session.ActiveShopId != shop ||
            !await sessions.IsValidAsync(session, ct))
            return Errors.Auth.SessionInvalidated;

        return await shops.GetActiveRoleAsync(actor, shop, ct) switch
        {
            ShopRole.Owner or ShopRole.Manager => null,
            null => Errors.Shop.MembershipNotFound,
            _ => roleRefusal ?? Errors.Inventory.UserIsNotOwnerOrManager,
        };
    }

    public async Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid actor, Guid shop, SessionClaims? session, CancellationToken ct, Error? roleRefusal = null)
    {
        if (await CheckAuthorityAsync(actor, shop, session, ct, roleRefusal) is { } denied)
            return denied;

        var lease = await fence.EnterAsync(shop, LicenceAction.NewTrading, ct);
        if (lease.IsError)
            return lease.Errors;

        if (await CheckWriteAsync(actor, shop, session, lease.Value, ct, roleRefusal) is { } refusal)
            return refusal;

        return lease.Value;
    }

    public async Task<Error?> CheckWriteAsync(Guid actor, Guid shop, SessionClaims? session, ShopLicenceLease lease, CancellationToken ct, Error? roleRefusal = null) =>
        await CheckAuthorityAsync(actor, shop, session, ct, roleRefusal) ?? lease.CurrentDecision().EnforcedDenial();
}
