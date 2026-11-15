using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Commercial billing accounts, delegated to licensing for the live verified session. Billing membership
/// grants no merchant shop access, so no shop or role policy applies here and none is granted.
/// </summary>
[ApiController]
[Route("api/billing/accounts")]
[Authorize]
public sealed class BillingController(IMerchantBillingAccess billing) : ControllerBase
{
    public sealed record CreateBillingAccountRequest(Guid OperationKey, string? Name);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? after, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await billing.ListAccountsAsync(caller, after, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    // Creation checks no credential and sends no message, so the caller's own budget is enough.
    [HttpPost]
    [RateLimit(Limit = 10, PeriodInMinutes = 60, BackoffMinutes = 15)]
    public async Task<IActionResult> Create([FromBody] CreateBillingAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await billing.CreateAccountAsync(caller,
            new MerchantCreateBillingAccount(request.OperationKey, request.Name ?? string.Empty), cancellationToken);
        if (result.IsError)
            return Failure(result.FirstError);
        return result.Value.Created
            ? StatusCode(StatusCodes.Status201Created, result.Value.Account)
            : Ok(result.Value.Account);
    }

    [HttpGet("{accountId:guid}/access")]
    public async Task<IActionResult> GetAccess(Guid accountId, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await billing.GetAsync(
            new MerchantBillingAccessRequest(accountId, caller.Session, caller.AccessExpiry), cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    internal static Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/billing"))
            return Task.CompletedTask;

        context.HandleResponse();
        var error = MerchantBillingErrors.SessionInvalid;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return context.Response.WriteAsJsonAsync(
            new PublicError(StatusCodes.Status401Unauthorized, error.Code, error.Description, SafeTraceId.Resolve(context.HttpContext)),
            options: null, contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted);
    }

    private MerchantBillingCaller? ReadCaller()
    {
        var session = SessionClaims.TryRead(User);
        return session is not null && long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            ? new MerchantBillingCaller(session, expires)
            : null;
    }

    private ObjectResult SessionInvalid()
    {
        var invalid = MerchantBillingErrors.SessionInvalid;
        return BillingProblem(StatusCodes.Status401Unauthorized, invalid.Code, invalid.Description);
    }

    private ObjectResult Failure(ErrorOr.Error problem)
    {
        var status = problem.Type switch
        {
            ErrorOr.ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorOr.ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorOr.ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorOr.ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return BillingProblem(status, problem.Code, problem.Description);
    }

    private ObjectResult BillingProblem(int status, string code, string title) => new(
        new PublicError(status, code, title, SafeTraceId.Resolve(HttpContext)))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" },
    };
}
