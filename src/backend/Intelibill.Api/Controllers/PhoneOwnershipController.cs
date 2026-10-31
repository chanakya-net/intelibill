using System.Globalization;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Verified phone authentication. One endpoint pair per purpose rather than a purpose field, so a
/// request cannot be pointed at a different flow than the one it was rate-limited and reasoned
/// about as.
/// </summary>
[ApiController]
[Route("api/auth/phone")]
public sealed class PhoneOwnershipController(IPhoneOwnershipService phones, IPhoneAuthPolicy policy) : ControllerBase
{
    /// <summary>
    /// Whether this deployment offers phone sign-in at all, so a client can hide the option rather
    /// than lead somebody into a route that will refuse them.
    /// </summary>
    [HttpGet("availability")]
    public IActionResult Availability() => Ok(new { enabled = policy.Enabled });

    [HttpPost("enrollment/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> RequestEnrollment(PhoneChallengeRequest request, CancellationToken ct) =>
        Receipt(await phones.RequestEnrollmentAsync(request.PhoneNumber, ct));

    [HttpPost("enrollment/confirm")]
    [RateLimit(Limit = 10, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ConfirmEnrollment(PhoneEnrollmentConfirmation request, CancellationToken ct) =>
        Session(await phones.ConfirmEnrollmentAsync(request.ChallengeId, request.Code, request.FirstName, request.LastName, ct));

    [HttpPost("login/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> RequestLogin(PhoneChallengeRequest request, CancellationToken ct) =>
        Receipt(await phones.RequestLoginAsync(request.PhoneNumber, ct));

    [HttpPost("login/confirm")]
    [RateLimit(Limit = 10, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ConfirmLogin(PhoneConfirmationRequest request, CancellationToken ct) =>
        Session(await phones.ConfirmLoginAsync(request.ChallengeId, request.Code, ct));

    [Authorize]
    [HttpPost("change/request")]
    [RateLimit(Limit = 5, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> RequestChange(PhoneChangeRequest request, CancellationToken ct) =>
        Receipt(await phones.RequestPhoneChangeAsync(Actor, Version, request.PhoneNumber, request.ReauthProof, ct));

    [Authorize]
    [HttpPost("change/confirm")]
    [RateLimit(Limit = 10, PeriodInMinutes = 15, BackoffMinutes = 15)]
    public async Task<IActionResult> ConfirmChange(PhoneConfirmationRequest request, CancellationToken ct)
    {
        var result = await phones.ConfirmPhoneChangeAsync(Actor, Version, request.ChallengeId, request.Code, ct);
        return result.IsError && result.FirstError.Type == ErrorType.Failure
            ? result.FirstError.ToUnavailableResult()
            : result.ToActionResult(_ => Ok(new { message = "Phone number updated. Please sign in again." }));
    }

    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private long Version => long.Parse(User.FindFirstValue("security_version")!, CultureInfo.InvariantCulture);

    /// <summary>
    /// Accepted whether or not a code was actually queued. The receipt's shape is the same either
    /// way, so this endpoint cannot be used to find out which numbers have accounts behind them.
    /// </summary>
    private IActionResult Receipt(ErrorOr<PhoneChallengeReceipt> result) =>
        result.IsError && result.FirstError.Type == ErrorType.Failure
            ? result.FirstError.ToUnavailableResult()
            : result.ToActionResult(Accepted);

    private IActionResult Session<T>(ErrorOr<T> result) =>
        result.IsError && result.FirstError.Type == ErrorType.Failure
            ? result.FirstError.ToUnavailableResult()
            : result.ToActionResult(value => Ok(value));
}

public sealed record PhoneChallengeRequest(string PhoneNumber);

public sealed record PhoneConfirmationRequest(Guid ChallengeId, string Code);

public sealed record PhoneEnrollmentConfirmation(Guid ChallengeId, string Code, string FirstName, string LastName);

public sealed record PhoneChangeRequest(string PhoneNumber, string? ReauthProof = null);
