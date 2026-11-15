using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Antiforgery;

namespace Intelibill.Licensing.Api.AdminIdentities;

/// <summary>Exact provider origin, browser authority and request token before any budget or write.</summary>
internal sealed class InvitationRequestGate(
    IAntiforgery antiforgery, IBootstrapRateBudget budget, TimeProvider clock, IHostEnvironment environment)
{
    internal async Task<IResult?> CheckAsync(HttpContext context, string providerOrigin, bool isAcceptance)
    {
        if (!BootstrapHttp.MatchesHost(context, providerOrigin, environment)
            || !string.Equals(context.Request.Headers.Origin.ToString(), providerOrigin, StringComparison.Ordinal)
            || !await antiforgery.IsRequestValidAsync(context))
            return Refused(context);

        var now = clock.GetUtcNow();
        var retryAt = await budget.TakeInvitationAsync(isAcceptance, now, context.RequestAborted);
        return retryAt is null ? null : BootstrapHttp.Throttled(context, retryAt.Value, now, "Admin.Throttled");
    }

    internal static IResult Refused(HttpContext context) =>
        BootstrapHttp.Problem(context, 403, "Admin.RequestRejected", "The request was refused. Reload the page and try again.");
}
