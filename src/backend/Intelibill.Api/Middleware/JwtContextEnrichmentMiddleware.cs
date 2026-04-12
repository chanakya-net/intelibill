using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Serilog.Context;

namespace Intelibill.Api.Middleware;

public sealed class JwtContextEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;

        if (user.Identity is not { IsAuthenticated: true })
        {
            await next(context);
            return;
        }

        var userId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var shopId = user.FindFirst("shop_id")?.Value;
        var tenantId = user.FindFirst("tenant_id")?.Value;

        // Collect IDisposable handles — disposed in finally after response completes
        var disposables = new List<IDisposable>(3);

        if (userId is not null)
        {
            disposables.Add(LogContext.PushProperty("UserId", userId));
            Activity.Current?.SetTag("user.id", userId);
        }

        if (shopId is not null)
        {
            disposables.Add(LogContext.PushProperty("ShopId", shopId));
            Activity.Current?.SetTag("shop.id", shopId);
        }

        if (tenantId is not null)
        {
            disposables.Add(LogContext.PushProperty("TenantId", tenantId));
            Activity.Current?.SetTag("tenant.id", tenantId);
        }

        try
        {
            await next(context);
        }
        finally
        {
            foreach (var d in disposables)
                d.Dispose();
        }
    }
}
