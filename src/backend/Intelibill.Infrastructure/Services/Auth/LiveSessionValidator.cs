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

        // A session whose account has moved to a new security version, or that was stamped under a
        // different recovery epoch, is already gone without anything having had to find and rewrite
        // the row.
        var sessionIsLive = await db.AuthSessions
            .AsNoTracking()
            .AnyAsync(
                s => s.Id == claims.SessionId
                    && s.UserId == claims.UserId
                    && s.RevokedAt == null
                    && s.ExpiresAt > now
                    && s.SecurityVersion == claims.SecurityVersion
                    && s.RecoveryEpoch == epoch
                    // The shop the token names must be the shop the device is in. Without this a
                    // tab left open before a switch would go on recording sales against the
                    // previous shop for the rest of the token's life, which in a billing system is
                    // the wrong shop's books. The scope version is deliberately not compared, so a
                    // client holding more than one token for the shop it is actually in keeps all
                    // of them working.
                    && s.ActiveShopId == claims.ActiveShopId,
                cancellationToken);

        if (!sessionIsLive)
            return false;

        var accountIsLive = await db.Users.AsNoTracking().AnyAsync(
            u => u.Id == claims.UserId
                && u.SecurityVersion == claims.SecurityVersion
                && u.IsLoginEnabled
                && (u.Email == null || u.IsEmailVerified),
            cancellationToken);

        if (!accountIsLive)
            return false;

        if (claims.ActiveShopId is null)
            return true;

        if (!Enum.TryParse<ShopRole>(claims.ActiveShopRole, ignoreCase: true, out var claimedRole))
            return false;

        // The role is checked live rather than trusted from the claim. Suspending or removing one
        // membership therefore ends access to that shop at once while the same account's sessions
        // scoped to its other shops keep working, and an Owner or Manager role recorded in an older
        // token cannot authorize anything after the membership that granted it changed.
        return await db.ShopMemberships.AsNoTracking().AnyAsync(
            sm => sm.UserId == claims.UserId
                && sm.ShopId == claims.ActiveShopId.Value
                && sm.Status == MembershipStatus.Active
                && sm.Role == claimedRole,
            cancellationToken);
    }
}
