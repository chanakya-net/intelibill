using System.Reflection;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Api.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class RateLimitPolicyResolverTests
{
    [Fact]
    public void Resolve_WhenNoOverride_ReturnsConfiguredDefaults()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 123,
            PeriodInMinutes = 7,
            BackoffMinutes = 9
        });

        var resolver = new RateLimitPolicyResolver(options);

        var policy = resolver.Resolve(CreateContext(new ActionDescriptor()));

        Assert.Equal(123, policy.Limit);
        Assert.Equal(7, policy.PeriodInMinutes);
        Assert.Equal(9, policy.BackoffMinutes);
    }

    [Fact]
    public void Resolve_WhenOverrideExists_ReturnsOverrideValues()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 100,
            PeriodInMinutes = 1,
            BackoffMinutes = 3
        });

        var resolver = new RateLimitPolicyResolver(options);

        var metadata = new List<object>
        {
            new RateLimitAttribute
            {
                Limit = 10,
                PeriodInMinutes = 2,
                BackoffMinutes = 4
            }
        };

        var policy = resolver.Resolve(CreateContext(new ActionDescriptor { EndpointMetadata = metadata }));

        Assert.Equal(10, policy.Limit);
        Assert.Equal(2, policy.PeriodInMinutes);
        Assert.Equal(4, policy.BackoffMinutes);
    }

    [Fact]
    public void Resolve_WhenMvcActionHasOverride_ReturnsOverrideValues()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 100,
            PeriodInMinutes = 1,
            BackoffMinutes = 3
        });

        var resolver = new RateLimitPolicyResolver(options);
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(TestController).GetTypeInfo(),
            MethodInfo = typeof(TestController).GetMethod(nameof(TestController.Limited))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.Equal(10, policy.Limit);
        Assert.Equal(2, policy.PeriodInMinutes);
        Assert.Equal(4, policy.BackoffMinutes);
    }

    private static ActionExecutingContext CreateContext(ActionDescriptor actionDescriptor)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            actionDescriptor);

        return new ActionExecutingContext(
            actionContext,
            filters: [],
            actionArguments: new Dictionary<string, object?>(),
            controller: new object());
    }

    private sealed class TestController
    {
        [RateLimit(Limit = 10, PeriodInMinutes = 2, BackoffMinutes = 4)]
        public static void Limited()
        {
        }
    }
}
