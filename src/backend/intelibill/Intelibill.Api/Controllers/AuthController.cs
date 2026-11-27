using Intelibill.Application.Common.Licensing;
using Intelibill.Api.Extensions;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Application.Features.Auth.Commands.RefreshToken;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Application.Features.Auth.Commands.ResetPassword;
using Intelibill.Application.Features.Auth.Commands.RevokeToken;
using Intelibill.Application.Features.Auth.DTOs;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IMessageBus bus) : ControllerBase
{
    // ── Login ────────────────────────────────────────────────────────────

    [HttpPost("login")]
    [RateLimit(Limit = 10, PeriodInMinutes = 1, BackoffMinutes = 3, Subject = RateLimitSubject.Account, FailClosed = true)]
    [LicenceAction(LicenceAction.Authentication)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new LoginCommand(request.Identifier, request.Password),
            cancellationToken);

        return result.ToActionResult(auth => Ok(auth));
    }

    // ── Password reset ────────────────────────────────────────────────────────

    [HttpPost("password-reset/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15, Subject = RateLimitSubject.Destination, FailClosed = true)]
    [LicenceAction(LicenceAction.Authentication)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new RequestPasswordResetCommand(request.Email),
            cancellationToken);

        if (result.IsError && result.FirstError.Code == "Auth.EmailDeliveryUnavailable")
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails { Status = 503, Title = result.FirstError.Code, Detail = result.FirstError.Description });
        return result.ToActionResult(_ => Ok(new { message = "If an account with that email exists, a reset link has been sent." }));
    }

    [HttpPost("password-reset/confirm")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15, Subject = RateLimitSubject.Account, FailClosed = true)]
    [LicenceAction(LicenceAction.Authentication)]
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
    [LicenceAction(LicenceAction.Authentication)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new RefreshTokenCommand(request.RefreshToken, request.ActiveShopId),
            cancellationToken);

        return result.ToActionResult(auth => Ok(auth));
    }

    [HttpPost("token/revoke")]
    [LicenceAction(LicenceAction.Authentication)]
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

public sealed record LoginRequest(string Identifier, string Password) : IRateLimitSubject
{
    string? IRateLimitSubject.Subject => Identifier;
}

public sealed record RequestPasswordResetRequest(string Email) : IRateLimitSubject
{
    string? IRateLimitSubject.Subject => Email;
}

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword) : IRateLimitSubject
{
    string? IRateLimitSubject.Subject => Email;
}

// The refresh secret is left out of the string form: MVC logs action arguments that way at Trace,
// and a raised log level must not be enough to write live credentials to the log store.
public sealed record RefreshTokenRequest(string RefreshToken, Guid? ActiveShopId = null)
{
    public override string ToString() => $"{nameof(RefreshTokenRequest)} {{ ActiveShopId = {ActiveShopId} }}";
}

public sealed record RevokeTokenRequest(string RefreshToken)
{
    public override string ToString() => nameof(RevokeTokenRequest);
}
