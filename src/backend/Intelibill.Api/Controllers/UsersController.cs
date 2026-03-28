using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IMessageBus bus) : ControllerBase
{
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
}

public sealed record UpdateMyProfileRequest(string Email, string? PhoneNumber, string FirstName, string LastName);
public sealed record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);
