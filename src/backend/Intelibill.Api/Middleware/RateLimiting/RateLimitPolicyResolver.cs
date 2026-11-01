using Intelibill.Api.Options;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Middleware.RateLimiting;

public sealed class RateLimitPolicyResolver(IOptions<RateLimitingOptions> options) : IRateLimitPolicyResolver
{
    public RateLimitPolicy Resolve(ActionExecutingContext context)
    {
        if (HasDisableMetadata(context))
        {
            return RateLimitPolicy.Disabled;
        }

        var defaults = options.Value;
        var policy = new RateLimitPolicy(defaults.Limit, defaults.PeriodInMinutes, defaults.BackoffMinutes);

        var overrideAttribute = ResolveOverride(context);
        if (overrideAttribute is null)
        {
            return policy;
        }

        // Which subject an endpoint is counted against, and whether it refuses when the store is
        // down, are what the endpoint is; they survive relaxation, which only widens numbers.
        policy = policy with { Subject = overrideAttribute.Subject, FailClosed = overrideAttribute.FailClosed };

        // Local development asks for the default rather than each endpoint's hardened figure. The
        // opt-out above is still honoured: this widens a policy, it never invents one the code
        // declined to apply.
        if (defaults.RelaxEndpointPolicies)
        {
            return policy;
        }

        return policy with
        {
            Limit = overrideAttribute.Limit > 0 ? overrideAttribute.Limit : policy.Limit,
            PeriodInMinutes = overrideAttribute.PeriodInMinutes > 0 ? overrideAttribute.PeriodInMinutes : policy.PeriodInMinutes,
            BackoffMinutes = overrideAttribute.BackoffMinutes >= 0 ? overrideAttribute.BackoffMinutes : policy.BackoffMinutes,
        };
    }

    private static bool HasDisableMetadata(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<DisableRateLimitAttribute>().Any())
        {
            return true;
        }

        if (context.ActionDescriptor is not ControllerActionDescriptor controllerAction)
        {
            return false;
        }

        return controllerAction.MethodInfo.GetCustomAttributes(typeof(DisableRateLimitAttribute), inherit: true).Length > 0
            || controllerAction.ControllerTypeInfo.GetCustomAttributes(typeof(DisableRateLimitAttribute), inherit: true).Length > 0;
    }

    private static RateLimitAttribute? ResolveOverride(ActionExecutingContext context)
    {
        var endpointOverride = context.ActionDescriptor.EndpointMetadata.OfType<RateLimitAttribute>().FirstOrDefault();
        if (endpointOverride is not null)
        {
            return endpointOverride;
        }

        if (context.ActionDescriptor is not ControllerActionDescriptor controllerAction)
        {
            return null;
        }

        return controllerAction.MethodInfo.GetCustomAttributes(typeof(RateLimitAttribute), inherit: true)
            .OfType<RateLimitAttribute>()
            .FirstOrDefault()
            ?? controllerAction.ControllerTypeInfo.GetCustomAttributes(typeof(RateLimitAttribute), inherit: true)
                .OfType<RateLimitAttribute>()
                .FirstOrDefault();
    }
}
