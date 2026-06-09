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

    [NonAction]
    protected IActionResult? CheckAuthAndShop()
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;
        return CheckShop();
    }

}
