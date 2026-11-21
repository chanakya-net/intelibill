using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Api.Controllers;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Api.Extensions;

internal sealed class SuspensionAccessFilter(ISuspensionAccessReader reader) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var endpoint = http.GetEndpoint();
        var source = endpoint?.Metadata.GetMetadata<SuspensionAtSourceAttribute>();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null &&
            endpoint?.Metadata.GetMetadata<LicenceActionAttribute>() is { Action: var action } &&
            action is not (LicenceAction.Authentication or LicenceAction.Health or LicenceAction.Billing or
                LicenceAction.InternalService or LicenceAction.MemberRemoval) &&
            http.User.Identity?.IsAuthenticated == true &&
            SessionClaims.TryRead(http.User) is { } session &&
            (ReadShop(context) ?? session.ActiveShopId) is { } shop && !UsesAnotherScope(context))
        {
            if (source is not { CheckRecovery: false } &&
                await reader.ReadDenialAsync(session, shop, source is null ? action : LicenceAction.Recovery,
                    http.RequestAborted) is { } denied)
            {
                context.Result = new ObjectResult(new PublicError(StatusCodes.Status403Forbidden, denied.Code,
                    denied.Description, SafeTraceId.Resolve(http)))
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    ContentTypes = { "application/problem+json" },
                };
                return;
            }

            // Request authentication already checked live authority in the active shop.
            // An explicit target needs a separate membership check before entering its scope.
            var deliverySession = shop != session.ActiveShopId ? session : null;
            await GuardResponseAsync(http, deliverySession, shop, next);
            return;
        }

        await next();
    }

    private async Task GuardResponseAsync(HttpContext http, SessionClaims? session, Guid shop, ResourceExecutionDelegate next)
    {
        var original = http.Features.GetRequiredFeature<IHttpResponseBodyFeature>();
        var guarded = new SuspensionResponseBodyFeature(http, original, reader, session, shop);
        http.Features.Set<IHttpResponseBodyFeature>(guarded);
        try
        {
            var executed = await next();
            if (guarded.Stopped && executed.Exception?.GetBaseException() is SuspensionResponseStoppedException)
                executed.ExceptionHandled = true;
            else if (executed.Exception is null)
                await guarded.FinishAsync();
        }
        catch (SuspensionResponseStoppedException) when (guarded.Stopped)
        {
        }
        finally
        {
            http.Features.Set(original);
            await guarded.ReleaseWriterAsync();
        }
    }

    // These source-authorized operations target a new or invitation-owned shop, independent of the old scope.
    private static bool UsesAnotherScope(ResourceExecutingContext context) =>
        context.ActionDescriptor is ControllerActionDescriptor action &&
        (action.ControllerTypeInfo.AsType() == typeof(InvitationsController) ||
            action.ControllerTypeInfo.AsType() == typeof(ShopsController) && action.MethodInfo.Name == nameof(ShopsController.CreateShop));

    private static Guid? ReadShop(ResourceExecutingContext context) =>
        context.RouteData.Values.TryGetValue("shopId", out var value) && Guid.TryParse(value?.ToString(), out var shop)
            ? shop : null;
}
