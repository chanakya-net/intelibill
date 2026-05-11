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

    [Fact]
    public void Resolve_WhenMvcActionHasDisableMetadata_ReturnsDisabledPolicy()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(TestController).GetTypeInfo(),
            MethodInfo = typeof(TestController).GetMethod(nameof(TestController.DisabledAction))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.True(policy.IsDisabled);
    }

    [Fact]
    public void Resolve_WhenMvcControllerHasDisableMetadata_ReturnsDisabledPolicy()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(DisabledController).GetTypeInfo(),
            MethodInfo = typeof(DisabledController).GetMethod(nameof(DisabledController.AnyAction))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.True(policy.IsDisabled);
    }

    [Fact]
    public void Resolve_WhenEndpointMetadataHasDisableMetadata_ReturnsDisabledPolicy()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ActionDescriptor
        {
            EndpointMetadata = [new DisableRateLimitAttribute()]
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.True(policy.IsDisabled);
    }

    [Fact]
    public void Resolve_WhenNonDisabledActionHasOverride_ContinuesToReturnOverrideValues()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(TestController).GetTypeInfo(),
            MethodInfo = typeof(TestController).GetMethod(nameof(TestController.Limited))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.False(policy.IsDisabled);
        Assert.Equal(10, policy.Limit);
        Assert.Equal(2, policy.PeriodInMinutes);
        Assert.Equal(4, policy.BackoffMinutes);
    }

    [Fact]
    public void Resolve_WhenPasswordResetRequestEndpoint_ReturnsCorrectRateLimit()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(Intelibill.Api.Controllers.AuthController).GetTypeInfo(),
            MethodInfo = typeof(Intelibill.Api.Controllers.AuthController).GetMethod(nameof(Intelibill.Api.Controllers.AuthController.RequestPasswordReset))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.False(policy.IsDisabled);
        Assert.Equal(5, policy.Limit);
        Assert.Equal(15, policy.PeriodInMinutes);
        Assert.Equal(15, policy.BackoffMinutes);
    }

    [Fact]
    public void Resolve_WhenPasswordResetConfirmEndpoint_ReturnsCorrectRateLimit()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(Intelibill.Api.Controllers.AuthController).GetTypeInfo(),
            MethodInfo = typeof(Intelibill.Api.Controllers.AuthController).GetMethod(nameof(Intelibill.Api.Controllers.AuthController.ResetPassword))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.False(policy.IsDisabled);
        Assert.Equal(5, policy.Limit);
        Assert.Equal(15, policy.PeriodInMinutes);
        Assert.Equal(15, policy.BackoffMinutes);
    }

    private static RateLimitPolicyResolver CreateResolver()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 100,
            PeriodInMinutes = 1,
            BackoffMinutes = 3
        });

        return new RateLimitPolicyResolver(options);
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

        [DisableRateLimit]
        public static void DisabledAction()
        {
        }
    }

    [DisableRateLimit]
    private sealed class DisabledController
    {
        public static void AnyAction()
        {
        }
    }
}
