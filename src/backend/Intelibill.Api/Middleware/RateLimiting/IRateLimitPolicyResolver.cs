using Microsoft.AspNetCore.Mvc.Filters;

namespace Intelibill.Api.Middleware.RateLimiting;

public interface IRateLimitPolicyResolver
{
    RateLimitPolicy Resolve(ActionExecutingContext context);
}
