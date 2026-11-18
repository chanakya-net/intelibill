using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Intelibill.Api.Extensions;

internal sealed class SuspensionAccessFilter(ISuspensionAccessReader reader) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var endpoint = http.GetEndpoint();
        var source = endpoint?.Metadata.GetMetadata<SuspensionAtSourceAttribute>();
        if (source is { CheckRecovery: false })
        {
            await next();
            return;
        }

        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null &&
            endpoint?.Metadata.GetMetadata<LicenceActionAttribute>() is { Action: var action } &&
            action is not (LicenceAction.Authentication or LicenceAction.Health or LicenceAction.Billing or
                LicenceAction.InternalService or LicenceAction.MemberRemoval) &&
            http.User.Identity?.IsAuthenticated == true &&
            SessionClaims.TryRead(http.User) is { } session &&
            (ReadShop(context) ?? session.ActiveShopId) is { } shop &&
            await reader.ReadDenialAsync(session, shop, source is null ? action : LicenceAction.Recovery, http.RequestAborted) is { } denied)
        {
            context.Result = new ObjectResult(new PublicError(StatusCodes.Status403Forbidden, denied.Code,
                denied.Description, SafeTraceId.Resolve(http)))
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }

    private static Guid? ReadShop(ResourceExecutingContext context) =>
        context.RouteData.Values.TryGetValue("shopId", out var value) && Guid.TryParse(value?.ToString(), out var shop)
            ? shop : null;
}
