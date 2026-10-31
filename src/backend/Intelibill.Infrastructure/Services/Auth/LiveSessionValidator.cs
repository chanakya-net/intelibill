using Intelibill.Application.Common.Interfaces;
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
    TimeProvider clock)
{
    public async Task<bool> IsValidAsync(SessionClaims claims, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var now = clock.GetUtcNow();
        var epoch = await recoveryEpochs.GetCurrentAsync(ct);

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
                ct);

        if (!sessionIsLive)
            return false;

        var accountIsLive = await db.Users.AsNoTracking().AnyAsync(
            u => u.Id == claims.UserId
                && u.SecurityVersion == claims.SecurityVersion
                && u.IsLoginEnabled
                && (u.Email == null || u.IsEmailVerified),
            ct);

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
            ct);
    }
}

/// <summary>
/// The claims a request must carry to be considered at all. A token that names no session, or whose
/// identifiers do not parse, never produces one of these — it is refused before any lookup, so no
/// session is ever invented for a legacy or malformed token.
/// </summary>
public sealed record SessionClaims(
    Guid UserId,
    Guid SessionId,
    long SecurityVersion,
    long ScopeVersion,
    Guid? ActiveShopId,
    string? ActiveShopRole)
{
    public static SessionClaims? TryRead(System.Security.Claims.ClaimsPrincipal? principal)
    {
        if (principal is null)
            return null;

        if (!Guid.TryParse(Read(principal, System.Security.Claims.ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(Read(principal, "sid"), out var sessionId) ||
            !long.TryParse(Read(principal, "security_version"), out var securityVersion) ||
            !long.TryParse(Read(principal, "scope_version"), out var scopeVersion))
            return null;

        var shopClaim = Read(principal, "active_shop_id");
        Guid? activeShopId = null;
        if (shopClaim is { Length: > 0 })
        {
            if (!Guid.TryParse(shopClaim, out var parsedShopId))
                return null;
            activeShopId = parsedShopId;
        }

        return new SessionClaims(
            userId,
            sessionId,
            securityVersion,
            scopeVersion,
            activeShopId,
            Read(principal, "active_shop_role"));
    }

    private static string? Read(System.Security.Claims.ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;
}
