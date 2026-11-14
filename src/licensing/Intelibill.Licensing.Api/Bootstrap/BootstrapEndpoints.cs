using System.Globalization;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Bootstrap;

internal static class BootstrapEndpoints
{
    private const int MaxRequestBytes = 4096;

    internal static void MapBootstrap(this WebApplication app)
    {
        var group = app.MapGroup(BootstrapHttp.Path);
        group.MapGet("/context", GetContextAsync).AllowAnonymous();
        group.MapPost("", ConsumeAsync).AllowAnonymous();
        group.MapPost("/enrollment/resume", ResumeAsync).AllowAnonymous();
        group.MapGet("/enrollment", (HttpContext context) =>
        {
            var expires = context.User.FindFirst(BootstrapAuthorization.ExpiryClaim)!.Value;
            return EnrollmentResponse(DateTimeOffset.Parse(expires, CultureInfo.InvariantCulture));
        }).RequireAuthorization(BootstrapAuthorization.EnrollmentScheme);
    }

    private static async Task<IResult> GetContextAsync(HttpContext context, IBootstrapStore store,
        IAntiforgery antiforgery, TimeProvider clock, IHostEnvironment environment)
    {
        var state = await store.ReadAsync(context.RequestAborted);
        if (!BootstrapHttp.MatchesHost(context, state.AdminOrigin, environment))
            return Results.Ok(new { available = false, canResume = false });

        var available = state.IsAvailable(clock.GetUtcNow());
        var canResume = state.OwnerId is { } owner
            && await store.FindPendingOwnerAsync(owner, context.RequestAborted) is not null;
        var tokens = available || canResume ? antiforgery.GetTokens(context) : null;
        if (tokens?.CookieToken is { } cookie)
            context.Response.Cookies.Append(BootstrapHttp.CsrfCookieName, cookie,
                BootstrapHttp.Cookie(state.AdminOrigin!, BootstrapHttp.Path));
        return Results.Ok(new { available, canResume, requestToken = tokens?.RequestToken });
    }

    private static Task<IResult> ConsumeAsync(HttpContext context, BootstrapAdminHandler handler, BootstrapRequestGate gate) =>
        CompleteAsync(context, gate, isResume: false, async origin =>
        {
            var request = await ReadAsync<ProofRequest>(context);
            return await handler.BootstrapAsync(request?.ProofToken, request?.Password, origin, context.RequestAborted);
        });

    private static Task<IResult> ResumeAsync(HttpContext context, BootstrapAdminHandler handler, BootstrapRequestGate gate) =>
        CompleteAsync(context, gate, isResume: true, async origin =>
        {
            var request = await ReadAsync<ResumeRequest>(context);
            return await handler.ResumeAsync(request?.Email, request?.Password, origin, context.RequestAborted);
        });

    private static async Task<IResult> CompleteAsync(HttpContext context, BootstrapRequestGate gate, bool isResume,
        Func<string, Task<ErrorOr<EnrollmentGrant>>> submit)
    {
        var origin = context.Request.Headers.Origin.ToString();
        var rejection = await gate.CheckAsync(context, origin, isResume);
        if (rejection is not null) return rejection;

        if (context.Request.ContentLength > MaxRequestBytes)
            return BootstrapHttp.Problem(context, 413, "Bootstrap.RequestTooLarge", "The setup request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxRequestBytes;

        var result = await submit(origin);
        if (result.IsError)
        {
            var error = result.FirstError;
            var status = error.Type switch
            {
                ErrorType.Conflict => 409,
                ErrorType.Unauthorized => 401,
                _ => 400,
            };
            return BootstrapHttp.Problem(context, status, error.Code, error.Description);
        }

        var grant = result.Value;
        var cookie = BootstrapHttp.Cookie(origin, "/api/admin");
        cookie.MaxAge = TimeSpan.FromMinutes(15);
        context.Response.Cookies.Append(BootstrapHttp.CookieName, grant.Credential.Value, cookie);
        return EnrollmentResponse(grant.ExpiresAt);
    }

    private static IResult EnrollmentResponse(DateTimeOffset expiry) =>
        Results.Ok(new { state = "enrollmentRequired", expiresAt = expiry });

    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        if (!context.Request.HasJsonContentType()) return null;
        try
        {
            return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ProofRequest
    {
        public string? ProofToken { get; init; }
        public string? Password { get; init; }
    }

    private sealed class ResumeRequest
    {
        public string? Email { get; init; }
        public string? Password { get; init; }
    }
}
