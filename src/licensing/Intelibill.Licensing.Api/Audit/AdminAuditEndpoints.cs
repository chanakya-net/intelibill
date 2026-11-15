using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Api.Audit;

internal static class AdminAuditEndpoints
{
    internal static void MapAdminAudit(this WebApplication app) =>
        app.MapGet("/api/admin/audit", ReadAsync).RequireAuthorization(ProviderSessions.AuditPolicy)
            .WithMetadata(new AuditDenial(BootstrapAuditAction.AuditReadDenied));

    private static async Task<IResult> ReadAsync(HttpContext context, AdminAuditQuery query, IAdminAuditStore store)
    {
        var actor = ProviderSessions.ToActor(context.User)!;
        var parameters = context.Request.Query;
        if (parameters.TryGetValue("scope", out var scope)
            && (scope.Count != 1 || scope[0] != AdminAuditQuery.IdentityScope))
        {
            await store.DeniedAsync(actor.AdminId, BootstrapAuditAction.AuditReadDenied, AuditReason.ScopeUnavailable,
                context.RequestAborted);
            return BootstrapHttp.Problem(context, 403, "Admin.AccessDenied", "Provider access is unavailable.");
        }

        var page = await query.ReadAsync(actor.AdminId, Filter("action"), Filter("fromUtc"), Filter("toUtc"),
            Filter("pageSize"), Filter("cursor"), context.RequestAborted);
        return page.IsError
            ? BootstrapHttp.Problem(context, 400, page.FirstError.Code, page.FirstError.Description)
            : Results.Ok(page.Value);

        string? Filter(string name) => parameters.TryGetValue(name, out var value) ? value.ToString() : null;
    }
}

internal sealed record AuditDenial(BootstrapAuditAction Action);
