using System.Globalization;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class EmailOwnershipController(IEmailOwnershipService ownership) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private long Version => long.Parse(User.FindFirstValue("security_version")!, CultureInfo.InvariantCulture);
    private IActionResult AcceptedRequest(ErrorOr<Guid> result)
    {
        if (result.IsError && result.FirstError.Type == ErrorType.Failure)
            return Problem(statusCode: 503, title: result.FirstError.Code, detail: result.FirstError.Description);
        return result.ToActionResult(_ => Accepted(new { message = "If this address is eligible, a verification email will arrive shortly." }));
    }
    [HttpPost("enrollment/email/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> RequestEmail(EmailOwnershipRequest request, CancellationToken ct) => AcceptedRequest(await ownership.RequestAsync(request.Email, ct));
    [HttpPost("enrollment/email/confirm")]
    [RateLimit(Limit = 10, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ConfirmEnrollment(EmailEnrollmentConfirmation request, CancellationToken ct) =>
        (await ownership.ConfirmEnrollmentAsync(request.ChallengeId, request.Token, request.Password, request.FirstName, request.LastName, ct)).ToActionResult(Ok);
    [HttpPost("enrollment/email/claim/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> RequestClaim(EmailOwnershipRequest request, CancellationToken ct) => AcceptedRequest(await ownership.RequestClaimAsync(request.Email, ct));
    [HttpPost("enrollment/email/claim/confirm")]
    [RateLimit(Limit = 10, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ConfirmClaim(EmailEnrollmentConfirmation request, CancellationToken ct) =>
        (await ownership.ConfirmClaimAsync(request.ChallengeId, request.Token, request.Password, request.FirstName, request.LastName, ct)).ToActionResult(Ok);
    [Authorize]
    [HttpPost("reauthenticate")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> Reauthenticate(ReauthenticationRequest request, CancellationToken ct) =>
        (await ownership.ReauthenticateAsync(Actor, Version, request.Password, request.Provider, request.Token, ct)).ToActionResult(Ok);
    [Authorize]
    [HttpPost("reauthenticate/callback")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ReauthenticateCallback(IdentityOAuthCallback request, [FromServices] IExternalOAuthFlowService oauth, CancellationToken ct)
    {
        var exchange = await oauth.ExchangeCodeAsync(request.Code, request.State, ct);
        if (exchange.IsError) return exchange.Errors.ToProblemResult();
        return (await ownership.ReauthenticateAsync(Actor, Version, null, exchange.Value.Provider, exchange.Value.ProviderToken, ct)).ToActionResult(Ok);
    }
    [Authorize]
    [HttpPost("email-change/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> RequestChange(EmailChangeRequest request, CancellationToken ct) => AcceptedRequest(await ownership.RequestEmailChangeAsync(Actor, Version, request.Email, request.ReauthProof, ct));
    [Authorize]
    [HttpPost("email-change/confirm")]
    [RateLimit(Limit = 10, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ConfirmChange(EmailOwnershipConfirmation request, CancellationToken ct) =>
        (await ownership.ConfirmEmailChangeAsync(Actor, Version, request.ChallengeId, request.Token, ct)).ToActionResult(_ => Ok(new { message = "Email updated. Please sign in again." }));
    [Authorize]
    [HttpGet("external-links")]
    public async Task<IActionResult> Links([FromServices] IUserRepository users, CancellationToken ct)
    {
        var user = await users.GetByIdWithDetailsAsync(Actor, ct);
        return user is null ? Unauthorized() : Ok(new { providers = user.ExternalLogins.Select(x => x.Provider).Distinct(), hasPassword = user.IsEmailVerified && user.PasswordHash is not null });
    }
    [Authorize]
    [HttpPost("external-links")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> Link(IdentityLinkRequest request, CancellationToken ct) =>
        (await ownership.LinkAsync(Actor, Version, request.Provider, request.Token, request.ReauthProof, ct)).ToActionResult(_ => NoContent());
    [Authorize]
    [HttpPost("external-links/callback")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> LinkCallback(IdentityLinkCallback request, [FromServices] IExternalOAuthFlowService oauth, CancellationToken ct)
    {
        var exchange = await oauth.ExchangeCodeAsync(request.Code, request.State, ct);
        if (exchange.IsError) return exchange.Errors.ToProblemResult();
        return (await ownership.LinkAsync(Actor, Version, exchange.Value.Provider, exchange.Value.ProviderToken, request.ReauthProof, ct)).ToActionResult(_ => NoContent());
    }
    [Authorize]
    [HttpDelete("external-links/{provider}")]
    public async Task<IActionResult> Unlink(ExternalAuthProvider provider, [FromBody] IdentityUnlinkRequest request, CancellationToken ct) =>
        (await ownership.UnlinkAsync(Actor, Version, provider, request.ReauthProof, ct)).ToActionResult(_ => NoContent());
}
public sealed record EmailOwnershipRequest(string Email);
public sealed record EmailEnrollmentConfirmation(Guid ChallengeId, string Token, string Password, string FirstName, string LastName);
public sealed record EmailOwnershipConfirmation(Guid ChallengeId, string Token);
public sealed record ReauthenticationRequest(string? Password, ExternalAuthProvider? Provider, string? Token);
public sealed record EmailChangeRequest(string Email, string ReauthProof);
public sealed record IdentityLinkRequest(ExternalAuthProvider Provider, string Token, string ReauthProof);
public sealed record IdentityLinkCallback(string Code, string State, string ReauthProof);
public sealed record IdentityOAuthCallback(string Code, string State);
public sealed record IdentityUnlinkRequest(string ReauthProof);
