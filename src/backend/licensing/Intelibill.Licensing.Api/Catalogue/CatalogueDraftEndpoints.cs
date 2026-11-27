using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Catalogue;

namespace Intelibill.Licensing.Api.Catalogue;

/// <summary>
/// Provider-only draft catalogue. Reads and writes need a live provider session whose administrator holds the
/// persisted Licensing Manager grant; writes also need the admin origin and CSRF proof. Drafts never leave here.
/// </summary>
internal static class CatalogueDraftEndpoints
{
    private const int MaxRequestBytes = 4096;

    // Numbers must be JSON numbers, every member must be known and appear once: a typo can never become a
    // silently missing (or defaulted) commercial fact.
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    internal static void MapCatalogueDrafts(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(ProviderSessions.CataloguePolicy);
        group.MapGet("/plans", ListPlansAsync);
        group.MapPut("/plans/{planId:guid}/draft", SavePlanAsync);
        group.MapGet("/trial-policy/draft", GetTrialAsync);
        group.MapPut("/trial-policy/draft", SaveTrialAsync);
        group.MapCataloguePublication();
        group.MapCatalogueRetirement();
    }

    private static async Task<IResult> ListPlansAsync(HttpContext context, SavePlanDraftHandler handler, Guid? cursor)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.ListAsync(caller, cursor, context.RequestAborted);
        return result.IsError ? Problem(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> SavePlanAsync(HttpContext context, Guid planId, SavePlanDraftHandler handler,
        AdminSignInHttp http)
    {
        if (await AcceptAsync(context, http) is { } refused) return refused;
        var body = await ReadAsync<PlanDraftRequest>(context);
        if (body.Refusal is not null) return body.Refusal;
        var result = await handler.SaveAsync(body.Caller!, planId, body.Value, CancellationToken.None);
        return result.IsError ? Problem(context, result.FirstError)
            : Results.Json(result.Value.Item, statusCode: result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> GetTrialAsync(HttpContext context, SaveTrialPolicyDraftHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.GetAsync(caller, context.RequestAborted);
        return result.IsError ? Problem(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> SaveTrialAsync(HttpContext context, SaveTrialPolicyDraftHandler handler,
        AdminSignInHttp http)
    {
        if (await AcceptAsync(context, http) is { } refused) return refused;
        var body = await ReadAsync<TrialPolicyDraftRequest>(context);
        if (body.Refusal is not null) return body.Refusal;
        var result = await handler.SaveAsync(body.Caller!, body.Value, CancellationToken.None);
        return result.IsError ? Problem(context, result.FirstError)
            : Results.Json(result.Value.Body, statusCode: result.Value.Created ? 201 : 200);
    }

    internal static async Task<IResult?> AcceptAsync(HttpContext context, AdminSignInHttp http)
    {
        var origin = await http.ReadOriginAsync(context);
        return origin is null || !http.AcceptsMutation(context, origin) ? InvitationRequestGate.Refused(context) : null;
    }

    internal static async Task<(AdminSessionCaller? Caller, T? Value, IResult? Refusal)> ReadAsync<T>(HttpContext context,
        int maxRequestBytes = MaxRequestBytes)
        where T : class
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return (null, null, Problem(context, AdminRefreshErrors.SessionEnded));
        if (context.Request.ContentLength > maxRequestBytes) return (caller, null, TooLarge(context));
        var buffer = new byte[maxRequestBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length
               && (read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), context.RequestAborted)) > 0)
            length += read;
        if (length > maxRequestBytes) return (caller, null, TooLarge(context));
        if (!context.Request.HasJsonContentType()) return (caller, null, null);
        try
        {
            return (caller, JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), Strict), null);
        }
        catch (JsonException)
        {
            return (caller, null, null);
        }
    }

    private static IResult TooLarge(HttpContext context) =>
        BootstrapHttp.Problem(context, 413, "Admin.RequestTooLarge", "The request is too large.");

    internal static IResult Problem(HttpContext context, Error error) =>
        BootstrapHttp.Problem(context, error.Type switch
        {
            ErrorType.Unauthorized => 401,
            ErrorType.Forbidden => 403,
            ErrorType.NotFound => 404,
            ErrorType.Conflict => 409,
            ErrorType.Failure => 503,
            _ => 400,
        }, error.Code, error.Description);
}
