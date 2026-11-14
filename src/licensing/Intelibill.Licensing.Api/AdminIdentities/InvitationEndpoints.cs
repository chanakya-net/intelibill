using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static partial class InvitationEndpoints
{
    internal const string OwnerPath = "/api/admin/invitations";
    internal const string AcceptancePath = "/api/admin/auth/invitation";
    private const int MaxRequestBytes = 4096;

    internal static void MapInvitations(this WebApplication app)
    {
        var owner = app.MapGroup(OwnerPath).RequireAuthorization(ProviderSessions.InvitationPolicy);
        owner.MapGet("/context", OwnerContextAsync);
        owner.MapPost("", InviteAsync);

        // Public signup does not exist: acceptance needs an emailed one-use invitation.
        var acceptance = app.MapGroup(AcceptancePath).AllowAnonymous();
        acceptance.MapGet("/context", AcceptanceContextAsync);
        acceptance.MapPost("/accept", AcceptAsync);
    }

    private static async Task<IResult> OwnerContextAsync(HttpContext context, InviteAdminHandler handler,
        IAntiforgery antiforgery, IHostEnvironment environment)
    {
        var authority = await AuthorizeAsync(context, handler);
        if (authority.IsError) return Problem(context, authority.FirstError);
        if (!BootstrapHttp.MatchesHost(context, authority.Value, environment)) return InvitationRequestGate.Refused(context);
        return Results.Ok(new { requestToken = IssueRequestToken(context, antiforgery, authority.Value, OwnerPath) });
    }

    private static async Task<IResult> InviteAsync(HttpContext context, InviteAdminHandler handler, InvitationRequestGate gate,
        ILogger<InviteAdminHandler> logger)
    {
        var authority = await AuthorizeAsync(context, handler);
        if (authority.IsError) return Problem(context, authority.FirstError);
        var rejection = await CheckAsync(context, gate, authority.Value, isAcceptance: false);
        if (rejection is not null) return rejection;

        var request = await ReadAsync<InviteRequest>(context);
        var result = await handler.InviteAsync(ProviderSessions.ToActor(context.User)!, request?.Email, context.RequestAborted);
        if (result.IsError) return Problem(context, result.FirstError, logger);
        LogIssued(logger, result.Value.InvitationId);
        return Results.Json(new { invitationId = result.Value.InvitationId, expiresAt = result.Value.ExpiresAt },
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AcceptanceContextAsync(HttpContext context, IAdminInvitationStore store,
        IAntiforgery antiforgery, IHostEnvironment environment)
    {
        var origin = await store.FindProviderOriginAsync(context.RequestAborted);
        if (origin is null || !BootstrapHttp.MatchesHost(context, origin, environment))
            return Results.Ok(new { available = false });
        return Results.Ok(new { available = true, requestToken = IssueRequestToken(context, antiforgery, origin, AcceptancePath) });
    }

    private static async Task<IResult> AcceptAsync(HttpContext context, AcceptAdminInvitationHandler handler,
        IAdminInvitationStore store, InvitationRequestGate gate, ILogger<AcceptAdminInvitationHandler> logger)
    {
        var origin = await store.FindProviderOriginAsync(context.RequestAborted);
        if (origin is null) return InvitationRequestGate.Refused(context);
        var rejection = await CheckAsync(context, gate, origin, isAcceptance: true);
        if (rejection is not null) return rejection;

        var request = await ReadAsync<AcceptRequest>(context);
        var result = await handler.AcceptAsync(request?.InvitationToken, request?.Email, request?.Password, context.RequestAborted);
        if (result.IsError) return Problem(context, result.FirstError, logger);
        LogAccepted(logger);
        return BootstrapHttp.Enrollment(context, origin, result.Value);
    }

    private static async Task<ErrorOr<string>> AuthorizeAsync(HttpContext context, InviteAdminHandler handler)
    {
        var actor = ProviderSessions.ToActor(context.User);
        return actor is null
            ? Error.Forbidden("Admin.AccessDenied", "You cannot manage administrator invitations.")
            : await handler.AuthorizeAsync(actor, context.RequestAborted);
    }

    private static async Task<IResult?> CheckAsync(HttpContext context, InvitationRequestGate gate, string origin, bool isAcceptance)
    {
        var rejection = await gate.CheckAsync(context, origin, isAcceptance);
        if (rejection is not null) return rejection;
        if (context.Request.ContentLength > MaxRequestBytes)
            return BootstrapHttp.Problem(context, 413, "Admin.RequestTooLarge", "The request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxRequestBytes;
        return null;
    }

    private static string? IssueRequestToken(HttpContext context, IAntiforgery antiforgery, string origin, string path)
    {
        var tokens = antiforgery.GetTokens(context);
        if (tokens.CookieToken is { } cookie)
            context.Response.Cookies.Append(BootstrapHttp.CsrfCookieName, cookie, BootstrapHttp.Cookie(origin, path));
        return tokens.RequestToken;
    }

    private static IResult Problem(HttpContext context, Error error, ILogger logger)
    {
        LogRefused(logger, error.Code);
        return Problem(context, error);
    }

    private static IResult Problem(HttpContext context, Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Conflict => 409,
            ErrorType.Unauthorized => 401,
            ErrorType.Forbidden => 403,
            ErrorType.Failure => 503,
            _ => 400,
        };
        return BootstrapHttp.Problem(context, status, error.Code, error.Description);
    }

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

    // Identifiers and outcome codes only: never the email, link, token or request body.
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Admin invitation {InvitationId} issued.")]
    private static partial void LogIssued(ILogger logger, Guid invitationId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Admin invitation accepted; MFA enrollment pending.")]
    private static partial void LogAccepted(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Admin invitation request refused ({Outcome}).")]
    private static partial void LogRefused(ILogger logger, string outcome);

    private sealed class InviteRequest
    {
        public string? Email { get; init; }
    }

    private sealed class AcceptRequest
    {
        public string? InvitationToken { get; init; }
        public string? Email { get; init; }
        public string? Password { get; init; }
    }
}
