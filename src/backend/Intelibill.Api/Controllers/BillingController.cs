using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/billing/accounts")]
[Authorize]
public sealed class BillingController(IMerchantBillingAccess billing) : ControllerBase
{
    [HttpGet("{accountId:guid}/access")]
    public async Task<IActionResult> GetAccess(Guid accountId, CancellationToken cancellationToken)
    {
        var session = SessionClaims.TryRead(User);
        if (session is null || !long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
        {
            var invalid = MerchantBillingErrors.SessionInvalid;
            return BillingProblem(StatusCodes.Status401Unauthorized, invalid.Code, invalid.Description);
        }

        var result = await billing.GetAsync(new MerchantBillingAccessRequest(accountId, session, expires), cancellationToken);
        if (!result.IsError)
            return Ok(result.Value);

        var problem = result.FirstError;
        var status = problem.Type switch
        {
            ErrorOr.ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorOr.ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return BillingProblem(status, problem.Code, problem.Description);
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

    private ObjectResult BillingProblem(int status, string code, string title) => new(
        new PublicError(status, code, title, SafeTraceId.Resolve(HttpContext)))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" },
    };
}
