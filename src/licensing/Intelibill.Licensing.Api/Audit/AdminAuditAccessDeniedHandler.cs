using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Intelibill.Licensing.Api.Audit;

internal sealed class AdminAuditAccessDeniedHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _standard = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var audit = context.GetEndpoint()?.Metadata.GetMetadata<AuditDenial>();
        if (authorizeResult.Forbidden && audit is not null)
            await RecordAsync(context, audit.Action, AuditReason.CapabilityRequired);

        await _standard.HandleAsync(next, context, policy, authorizeResult);
    }

    internal static Task RecordAsync(HttpContext context, BootstrapAuditAction action, AuditReason reason)
    {
        var liveProvider = context.User.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == ProviderSessions.Scheme);
        var actor = liveProvider is null ? null : ProviderSessions.ToActor(new System.Security.Claims.ClaimsPrincipal(liveProvider));
        return actor is null ? Task.CompletedTask
            : context.RequestServices.GetRequiredService<IAdminAuditStore>()
                .DeniedAsync(actor.AdminId, action, reason, context.RequestAborted);
    }
}
