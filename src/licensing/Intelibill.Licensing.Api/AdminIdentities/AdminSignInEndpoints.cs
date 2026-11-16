using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static class AdminSignInEndpoints
{
    private const int MaxRequestBytes = 4096;

    internal static void MapAdminSignIn(this WebApplication app)
    {
        var signIn = app.MapGroup("/api/admin/auth/sign-in").AllowAnonymous();
        signIn.MapGet("/context", ReadContextAsync);
        signIn.MapPost("", SignInAsync);
        app.MapGet("/api/admin/auth/session", ReadSession).RequireAuthorization(BootstrapAuthorization.BusinessPolicy);
    }

    private static async Task<IResult> ReadContextAsync(HttpContext context, AdminSignInHttp http, AdminAccessTokens tokens)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null || !tokens.IsConfigured) return SignInUnavailable(context);
        return Results.Ok(new { requestToken = http.IssueCsrf(context, origin) });
    }

    private static async Task<IResult> SignInAsync(HttpContext context, AdminSignInHttp http, AdminAccessTokens tokens,
        AdminSignInHandler handler, IAdminSignInBudget budget, TimeProvider clock)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null || !tokens.IsConfigured) return SignInUnavailable(context);
        if (!http.AcceptsMutation(context, origin)) return InvitationRequestGate.Refused(context);
        if (context.Request.ContentLength > MaxRequestBytes)
            return BootstrapHttp.Problem(context, 413, "Admin.RequestTooLarge", "The request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxRequestBytes;

        var request = await ReadRequestAsync(context);
        var subject = BootstrapValidation.NormalizeEmail(request?.Email) ?? "";
        var now = clock.GetUtcNow();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
        if (await budget.TakeAsync(digest, now, context.RequestAborted) is { } retryAt)
            return BootstrapHttp.Throttled(context, retryAt, now, "Admin.Throttled");

        var result = await handler.HandleAsync(request?.Email, request?.Password, request?.Otp, context.RequestAborted);
        if (result.IsError) return Problem(context, result.FirstError, clock.GetUtcNow());
        var grant = result.Value;
        AdminSignInHttp.WriteSession(context, origin, tokens.Create(grant.Session, grant.AccessIssuedAt), grant);
        return Results.Ok(new { accessExpiresAt = grant.AccessIssuedAt + AdminSession.AccessLifetime });
    }

    private static IResult ReadSession(HttpContext context)
    {
        var identity = context.User.Identities.FirstOrDefault(item => item.AuthenticationType == ProviderSessions.Scheme);
        if (identity is null) return Results.Unauthorized();
        return Results.Ok(new
        {
            capabilities = identity.FindAll(ProviderSessions.CapabilityClaim).Select(claim => claim.Value).Distinct(StringComparer.Ordinal),
            adminId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            sessionId = identity.FindFirst(ProviderSessions.SessionIdClaim)?.Value,
            accessExpiresAt = identity.FindFirst(ProviderSessions.AccessExpiresClaim)?.Value,
        });
    }

    private static async Task<SignInRequest?> ReadRequestAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) return null;
        try
        {
            return await context.Request.ReadFromJsonAsync<SignInRequest>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult SignInUnavailable(HttpContext context) =>
        BootstrapHttp.Problem(context, 503, "Admin.SignInUnavailable", "Provider sign-in is unavailable.");

    private static IResult Problem(HttpContext context, Error error, DateTimeOffset now)
    {
        if (error.Metadata?.TryGetValue("retryAt", out var retryAt) == true && retryAt is DateTimeOffset instant)
            return BootstrapHttp.Throttled(context, instant, now, "Admin.Throttled");
        return BootstrapHttp.Problem(context, error.Type == ErrorType.Failure ? 503 : 401, error.Code, error.Description);
    }

    private sealed class SignInRequest
    {
        public string? Email { get; init; }
        public string? Password { get; init; }
        public string? Otp { get; init; }
    }
}
