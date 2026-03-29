using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.Commands.AddShopUser;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Application.Features.Users.Queries.GetShopUsers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetShopUsers(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var shopId = GetCurrentActiveShopId();
        if (shopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<IReadOnlyList<ShopUserDto>>>(
            new GetShopUsersQuery(userId.Value, shopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddShopUser([FromBody] AddShopUserRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var shopId = GetCurrentActiveShopId();
        if (shopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<ShopUserDto>>(
            new AddShopUserCommand(
                userId.Value,
                shopId.Value,
                request.FirstName,
                request.LastName,
                request.PhoneNumber,
                request.Password,
                request.ConfirmPassword,
                request.Role),
            cancellationToken);

        return result.ToActionResult(user => CreatedAtAction(nameof(GetShopUsers), user));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new UpdateMyProfileCommand(userId.Value, request.Email, request.PhoneNumber, request.FirstName, request.LastName),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new ChangeMyPasswordCommand(userId.Value, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return result.ToActionResult(_ => Ok(new { message = "Password changed successfully." }));
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private Guid? GetCurrentActiveShopId()
    {
        var activeShopId = User.FindFirst("active_shop_id")?.Value;
        return Guid.TryParse(activeShopId, out var shopId) ? shopId : null;
    }
}

public sealed record AddShopUserRequest(string FirstName, string LastName, string PhoneNumber, string Password, string ConfirmPassword, string Role);
public sealed record UpdateMyProfileRequest(string Email, string? PhoneNumber, string FirstName, string LastName);
public sealed record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);
