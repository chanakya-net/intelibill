using System.Security.Claims;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static class AdminMfaEndpoints
{
    internal static void MapAdminMfa(this WebApplication app)
    {
        var group = app.MapGroup(BootstrapHttp.Path + "/enrollment/mfa")
            .RequireAuthorization(BootstrapAuthorization.EnrollmentScheme);
        group.MapGet("", ContextAsync);
        group.MapPost("/start", StartAsync);
        group.MapPost("/verify", VerifyAsync);
    }

    private static async Task<IResult> ContextAsync(HttpContext context, IBootstrapStore bootstrap, IAntiforgery antiforgery)
    {
        var state = await bootstrap.ReadAsync(context.RequestAborted);
        var tokens = antiforgery.GetTokens(context);
        if (tokens.CookieToken is { } cookie)
            context.Response.Cookies.Append(BootstrapHttp.CsrfCookieName, cookie,
                BootstrapHttp.Cookie(state.AdminOrigin!, BootstrapHttp.Path));
        return Results.Ok(new { requestToken = tokens.RequestToken });
    }

    private static async Task<IResult> StartAsync(HttpContext context, IBootstrapStore bootstrap,
        IAntiforgery antiforgery, IBootstrapSecrets secrets, IAdminMfaEnrollment enrollment, IHostEnvironment environment)
    {
        if (!await IsValidRequestAsync(context, bootstrap, antiforgery, environment)) return Rejected(context);
        var (admin, digest) = Enrollment(context, secrets);
        var result = await enrollment.StartAsync(admin, digest, context.RequestAborted);
        return result.IsError ? Problem(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> VerifyAsync(HttpContext context, IBootstrapStore bootstrap,
        IAntiforgery antiforgery, IBootstrapSecrets secrets, IAdminMfaEnrollment enrollment, IHostEnvironment environment, TimeProvider clock)
    {
        if (!await IsValidRequestAsync(context, bootstrap, antiforgery, environment)) return Rejected(context);
        const int maxBytes = 1024;
        if (context.Request.ContentLength > maxBytes)
            return BootstrapHttp.Problem(context, 413, "Mfa.RequestTooLarge", "The verification request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = maxBytes;
        var otp = await ReadOtpAsync(context);
        var (admin, digest) = Enrollment(context, secrets);
        // Once the body and request context are accepted, persist the attempt even if the browser disconnects.
        var result = await enrollment.CompleteAsync(admin, digest, otp, CancellationToken.None);
        if (result.IsError)
        {
            var error = result.FirstError;
            if (error.NumericType == 429 && error.Metadata?["retryAt"] is DateTimeOffset retryAt)
                return BootstrapHttp.Throttled(context, retryAt, clock.GetUtcNow(), error.Code);
            return Problem(context, error);
        }

        var origin = context.Request.Headers.Origin.ToString();
        context.Response.Cookies.Delete(BootstrapHttp.CookieName, BootstrapHttp.Cookie(origin, "/api/admin"));
        return Results.Ok(new { state = "enrollmentComplete", recoveryCodes = result.Value.RecoveryCodes });
    }

    private static async Task<string?> ReadOtpAsync(HttpContext context)
    {
        try
        {
            return context.Request.HasJsonContentType()
                ? (await context.Request.ReadFromJsonAsync<ProofRequest>(context.RequestAborted))?.Otp
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (Guid AdminId, string Digest) Enrollment(HttpContext context, IBootstrapSecrets secrets) =>
        (Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            secrets.HashEnrollment(context.Request.Cookies[BootstrapHttp.CookieName]!));

    private static async Task<bool> IsValidRequestAsync(
        HttpContext context, IBootstrapStore bootstrap, IAntiforgery antiforgery, IHostEnvironment environment)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!BootstrapHttp.MatchesHost(context, origin, environment)) return false;
        var state = await bootstrap.ReadAsync(context.RequestAborted);
        return string.Equals(origin, state.AdminOrigin, StringComparison.Ordinal)
            && await antiforgery.IsRequestValidAsync(context);
    }

    private static IResult Rejected(HttpContext context) =>
        BootstrapHttp.Problem(context, 403, "Mfa.RequestRejected", "The verification request was refused.");

    private static IResult Problem(HttpContext context, Error error) =>
        BootstrapHttp.Problem(context, error.Type switch
        {
            ErrorType.Unauthorized => 401,
            ErrorType.Failure => 503,
            _ => 400,
        }, error.Code, error.Description);

    private sealed class ProofRequest
    {
        public string? Otp { get; init; }
    }
}
