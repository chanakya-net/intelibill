using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// What every protected request is checked against. A token proves only that this server signed it;
/// whether it still means anything is decided here, against the live session, account and
/// membership.
/// </summary>
public sealed class LiveSessionValidator(
    ApplicationDbContext db,
    IRecoveryEpochProvider recoveryEpochs,
    TimeProvider clock) : ILiveSessionAuthority
{
    public async Task<bool> IsValidAsync(SessionClaims claims, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var now = clock.GetUtcNow();
        var epoch = await recoveryEpochs.GetCurrentAsync(cancellationToken);

        // Judge the device, user and membership in one PostgreSQL statement. In addition to
        // avoiding round trips during transport handoffs, all three facts use the same snapshot.
        var sessions = db.AuthSessions.Where(session => session.Id == claims.SessionId &&
            session.UserId == claims.UserId && session.RevokedAt == null && session.ExpiresAt > now &&
            session.SecurityVersion == claims.SecurityVersion && session.RecoveryEpoch == epoch &&
            session.ActiveShopId == claims.ActiveShopId &&
            db.Users.Any(user => user.Id == session.UserId && user.SecurityVersion == claims.SecurityVersion &&
                user.IsLoginEnabled && (user.Email == null || user.IsEmailVerified)));

        // Scope version is deliberately omitted. Multiple valid tokens for the device's current
        // shop remain usable, while a token for a previously active shop fails the check above.
        if (claims.ActiveShopId is { } shopId)
        {
            if (!Enum.TryParse<ShopRole>(claims.ActiveShopRole, ignoreCase: true, out var role))
                return false;

            sessions = sessions.Where(session => db.ShopMemberships.Any(membership =>
                membership.UserId == session.UserId && membership.ShopId == shopId &&
                membership.Status == MembershipStatus.Active && membership.Role == role));
        }

        return await sessions.AnyAsync(cancellationToken);
    }
}
