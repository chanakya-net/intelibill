using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Api.Extensions;

internal sealed class HttpCurrentSessionContext(IHttpContextAccessor httpContextAccessor) : ICurrentSessionContext
{
    private const string ActiveShopClaim = "active_shop_id";

    public Guid? UserId =>
        TryReadGuidClaim(JwtRegisteredClaimNames.Sub)
        ?? TryReadGuidClaim(ClaimTypes.NameIdentifier);

    public Guid? ActiveShopId => TryReadGuidClaim(ActiveShopClaim);

    private Guid? TryReadGuidClaim(string claimType)
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}