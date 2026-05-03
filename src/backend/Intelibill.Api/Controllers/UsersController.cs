using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.Commands.AddShopUser;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Application.Features.Users.Commands.EditShopUser;
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
public sealed class UsersController : AuthenticatedControllerBase
{
    public UsersController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetShopUsers(CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<IReadOnlyList<ShopUserDto>>>(
            new GetShopUsersQuery(UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddShopUser([FromBody] AddShopUserRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopUserDto>>(
            new AddShopUserCommand(
                UserId!.Value,
                request.ShopIds,
                request.Email,
                request.FirstName,
                request.LastName,
                request.PhoneNumber,
                request.Password,
                request.ConfirmPassword,
                request.Role),
            cancellationToken);

        return result.ToActionResult(user => CreatedAtAction(nameof(GetShopUsers), user));
    }

    [HttpPut("{targetUserId:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> EditShopUser(Guid targetUserId, [FromBody] EditShopUserRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopUserDto>>(
            new EditShopUserCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                targetUserId,
                request.Email,
                request.FirstName,
                request.LastName,
                request.PhoneNumber,
                request.Role,
                request.IsLoginEnabled,
                request.ShopIds),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new UpdateMyProfileCommand(UserId!.Value, request.Email, request.PhoneNumber, request.FirstName, request.LastName, request.Language),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new ChangeMyPasswordCommand(UserId!.Value, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return result.ToActionResult(_ => Ok(new { message = "Password changed successfully." }));
    }
}

public sealed record AddShopUserRequest(IReadOnlyList<Guid> ShopIds, string Email, string FirstName, string LastName, string PhoneNumber, string Password, string ConfirmPassword, string Role);
public sealed record EditShopUserRequest(string Email, string FirstName, string LastName, string PhoneNumber, string Role, bool IsLoginEnabled, IReadOnlyList<Guid>? ShopIds = null);
public sealed record UpdateMyProfileRequest(string Email, string? PhoneNumber, string FirstName, string LastName, string Language = "en-IN");
public sealed record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);
