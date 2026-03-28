using Intelibill.Api.Extensions;
using Intelibill.Api.Options;
using Intelibill.Application.Features.Auth.Commands.ExternalLogin;
using Intelibill.Application.Features.Auth.Commands.LoginWithEmail;
using Intelibill.Application.Features.Auth.Commands.RefreshToken;
using Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;
using Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Application.Features.Auth.Commands.ResetPassword;
using Intelibill.Application.Features.Auth.Commands.RevokeToken;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IMessageBus bus, IOptions<AppOptions> appOptions) : ControllerBase
{
    // ── Registration ─────────────────────────────────────────────────────────

    [HttpPost("register/email")]
    public async Task<IActionResult> RegisterWithEmail(
        [FromBody] RegisterWithEmailRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new RegisterWithEmailCommand(request.Email, request.Password, request.FirstName, request.LastName),
            cancellationToken);

        return result.ToActionResult(auth => CreatedAtAction(nameof(RegisterWithEmail), auth));
    }

    [HttpPost("register/phone")]
    public async Task<IActionResult> RegisterWithPhone(
        [FromBody] RegisterWithPhoneRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new RegisterWithPhoneCommand(request.PhoneNumber, request.FirstName, request.LastName),
            cancellationToken);

        return result.ToActionResult(auth => CreatedAtAction(nameof(RegisterWithPhone), auth));
    }

    // ── Login ────────────────────────────────────────────────────────────

    [HttpPost("login/email")]
    public async Task<IActionResult> LoginWithEmail(
        [FromBody] LoginWithEmailRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new LoginWithEmailCommand(request.Email, request.Password),
            cancellationToken);

        return result.ToActionResult(auth => Ok(auth));
    }

    [HttpPost("login/external")]
    public async Task<IActionResult> ExternalLogin(
        [FromBody] ExternalLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new ExternalLoginCommand(request.Provider, request.Token, request.FirstName, request.LastName),
            cancellationToken);

        return result.ToActionResult(auth => Ok(auth));
    }

    // ── Password reset ────────────────────────────────────────────────────────

    [HttpPost("password-reset/request")]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new RequestPasswordResetCommand(request.Email, appOptions.Value.BaseUrl),
            cancellationToken);

        // Always return 200 to avoid revealing whether the email exists.
        return Ok(new { message = "If an account with that email exists, a reset link has been sent." });
    }

    [HttpPost("password-reset/confirm")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new ResetPasswordCommand(request.Email, request.Token, request.NewPassword),
            cancellationToken);

        return result.ToActionResult(_ => Ok(new { message = "Password has been reset successfully." }));
    }

    // ── Token management ──────────────────────────────────────────────────────

    [HttpPost("token/refresh")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new RefreshTokenCommand(request.RefreshToken),
            cancellationToken);

        return result.ToActionResult(auth => Ok(auth));
    }

    [HttpPost("token/revoke")]
    public async Task<IActionResult> RevokeToken(
        [FromBody] RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new RevokeTokenCommand(request.RefreshToken),
            cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }
}

// ── Request models ─────────────────────────────────────────────────────────────

public sealed record RegisterWithEmailRequest(string Email, string Password, string FirstName, string LastName);
public sealed record RegisterWithPhoneRequest(string PhoneNumber, string FirstName, string LastName);
public sealed record LoginWithEmailRequest(string Email, string Password);
public sealed record ExternalLoginRequest(ExternalAuthProvider Provider, string Token, string? FirstName, string? LastName);
public sealed record RequestPasswordResetRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record RevokeTokenRequest(string RefreshToken);
