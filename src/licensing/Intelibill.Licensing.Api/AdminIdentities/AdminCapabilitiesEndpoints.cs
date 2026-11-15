using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.Audit;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static class AdminCapabilitiesEndpoints
{
    private const int MaxRequestBytes = 4096;

    internal static void MapAdminCapabilities(this WebApplication app)
    {
        var users = app.MapGroup("/api/admin/users").RequireAuthorization(ProviderSessions.CapabilityPolicy)
            .WithMetadata(new AuditDenial(BootstrapAuditAction.AdminCapabilitiesDenied));
        users.MapGet("", ListAsync);
        users.MapPut("/{adminId:guid}/capabilities", UpdateAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext context, UpdateAdminCapabilitiesHandler handler,
        Guid? cursor)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.ListAsync(caller, cursor, context.RequestAborted);
        return result.IsError ? Problem(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateAsync(HttpContext context, Guid adminId,
        UpdateAdminCapabilitiesHandler handler, AdminSignInHttp http)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null || ProviderSessions.ReadCaller(context.User) is not { } caller
            || !http.AcceptsMutation(context, origin))
        {
            await AdminAuditAccessDeniedHandler.RecordAsync(context, BootstrapAuditAction.AdminCapabilitiesDenied,
                AuditReason.RequestRejected);
            return InvitationRequestGate.Refused(context);
        }

        if (context.Request.ContentLength > MaxRequestBytes)
            return BootstrapHttp.Problem(context, 413, "Admin.RequestTooLarge", "The request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxRequestBytes;

        var request = await ReadAsync(context);
        var result = await handler.UpdateAsync(caller, adminId, request, CancellationToken.None);
        return result.IsError ? Problem(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<AdminCapabilityUpdate?> ReadAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) return null;
        try
        {
            return await context.Request.ReadFromJsonAsync<AdminCapabilityUpdate>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Problem(HttpContext context, Error error) =>
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
