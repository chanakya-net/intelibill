using System.Globalization;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Antiforgery;

namespace Intelibill.Licensing.Api.Bootstrap;

internal sealed class BootstrapRequestGate(IBootstrapStore store, IAntiforgery antiforgery,
    IBootstrapRateBudget budget, TimeProvider clock, IHostEnvironment environment)
{
    internal async Task<IResult?> CheckAsync(HttpContext context, string origin, bool isResume)
    {
        if (!BootstrapHttp.MatchesHost(context, origin, environment))
            return Refused(context);

        var state = await store.ReadAsync(context.RequestAborted);
        if (!string.Equals(origin, state.AdminOrigin, StringComparison.Ordinal)
            || !await antiforgery.IsRequestValidAsync(context))
            return Refused(context);

        var now = clock.GetUtcNow();
        var retryAt = await budget.TakeAsync(isResume, now, context.RequestAborted);
        if (retryAt is null) return null;

        context.Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling((retryAt.Value - now).TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);
        return BootstrapHttp.Problem(context, 429, "Bootstrap.Throttled", "Too many setup attempts. Try again later.");
    }

    private static IResult Refused(HttpContext context) =>
        BootstrapHttp.Problem(context, 403, "Bootstrap.RequestRejected", "The setup request was refused.");
}
