using System.Security.Claims;

namespace Intelibill.Application.Common.Security;

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
    public static SessionClaims? TryRead(ClaimsPrincipal? principal)
    {
        if (principal is null)
            return null;

        if (!Guid.TryParse(Read(principal, ClaimTypes.NameIdentifier), out var userId) ||
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

    private static string? Read(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;
}
