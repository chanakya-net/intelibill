using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Extensions;

public abstract class AuthenticatedControllerBase : ControllerBase
{
    protected IMessageBus Bus { get; }

    protected AuthenticatedControllerBase(IMessageBus bus)
    {
        Bus = bus;
    }

    private Guid? TryGetGuidClaim(string claimType)
    {
        var value = User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    protected Guid? UserId => TryGetGuidClaim(JwtRegisteredClaimNames.Sub)
        ?? TryGetGuidClaim(ClaimTypes.NameIdentifier);

    protected long SecurityVersion => long.TryParse(User.FindFirst("security_version")?.Value, out var version) ? version : -1;

    /// <summary>
    /// The device session this request arrived on. Operations that change a session's scope act on
    /// this one only, so a shop switch on one device leaves the account's other devices alone.
    /// </summary>
    protected Guid? SessionId => TryGetGuidClaim("sid");

    protected Guid? ActiveShopId => TryGetGuidClaim("active_shop_id");

    protected string? ActiveShopRole => User.FindFirst("active_shop_role")?.Value;

    [NonAction]
    protected IActionResult? CheckAuth()
    {
        if (UserId is null)
            return Unauthorized();
        return null;
    }

    [NonAction]
    protected IActionResult? CheckShop()
    {
        if (ActiveShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();
        return null;
    }

    /// <summary>
    /// A request that names no device session cannot have one changed on its behalf. Live token
    /// validation already refuses such a token, so this is the belt to that braces.
    /// </summary>
    [NonAction]
    protected IActionResult? CheckSession()
    {
        if (SessionId is null)
            return Unauthorized();
        return null;
    }

    [NonAction]
    protected IActionResult? CheckAuthAndShop()
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;
        return CheckShop();
    }

}
