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

    /// <summary>
    /// Local relaxation drops the per-endpoint hardening back to the configured
    /// default. It does not switch limiting off: a runaway loop in development is
    /// still caught, it just takes a hundred requests rather than five.
    /// </summary>
    [Fact]
    public void Resolve_WhenEndpointPoliciesAreRelaxed_UsesTheConfiguredDefault()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 100,
            PeriodInMinutes = 1,
            BackoffMinutes = 3,
            RelaxEndpointPolicies = true
        });

        var resolver = new RateLimitPolicyResolver(options);
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(Intelibill.Api.Controllers.EmailOwnershipController).GetTypeInfo(),
            MethodInfo = typeof(Intelibill.Api.Controllers.EmailOwnershipController)
                .GetMethod(nameof(Intelibill.Api.Controllers.EmailOwnershipController.RequestEmail))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.False(policy.IsDisabled);
        Assert.Equal(100, policy.Limit);
        Assert.Equal(1, policy.PeriodInMinutes);
        Assert.Equal(3, policy.BackoffMinutes);
    }

    /// <summary>
    /// Relaxation widens a policy; it never creates one where the code asked for
    /// none, and never removes the one explicit opt-out.
    /// </summary>
    [Fact]
    public void Resolve_WhenEndpointPoliciesAreRelaxed_StillHonoursDisableRateLimit()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 100,
            PeriodInMinutes = 1,
            BackoffMinutes = 3,
            RelaxEndpointPolicies = true
        });

        var resolver = new RateLimitPolicyResolver(options);
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(TestController).GetTypeInfo(),
            MethodInfo = typeof(TestController).GetMethod(nameof(TestController.DisabledAction))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.True(policy.IsDisabled);
    }

    /// <summary>
    /// The default must be the hardened one: an environment that says nothing about
    /// relaxation gets the attribute's own limit.
    /// </summary>
    [Fact]
    public void Resolve_WhenRelaxationIsNotConfigured_KeepsTheEndpointPolicy()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(Intelibill.Api.Controllers.EmailOwnershipController).GetTypeInfo(),
            MethodInfo = typeof(Intelibill.Api.Controllers.EmailOwnershipController)
                .GetMethod(nameof(Intelibill.Api.Controllers.EmailOwnershipController.RequestEmail))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.Equal(5, policy.Limit);
        Assert.Equal(15, policy.PeriodInMinutes);
        Assert.Equal(15, policy.BackoffMinutes);
    }

    [Fact]
    public void Resolve_WhenEndpointDeclaresSubject_CarriesSubjectAndFailClosed()
    {
        var resolver = CreateResolver();
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(Intelibill.Api.Controllers.AuthController).GetTypeInfo(),
            MethodInfo = typeof(Intelibill.Api.Controllers.AuthController).GetMethod(nameof(Intelibill.Api.Controllers.AuthController.RequestPasswordReset))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.Equal(RateLimitSubject.Destination, policy.Subject);
        Assert.True(policy.FailClosed);
    }

    /// <summary>
    /// Relaxation widens numbers only: which subject an endpoint is limited by, and that it
    /// refuses rather than guesses when the store is down, stay what the endpoint declared.
    /// </summary>
    [Fact]
    public void Resolve_WhenEndpointPoliciesAreRelaxed_KeepsSubjectAndFailClosed()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions
        {
            Limit = 100,
            PeriodInMinutes = 1,
            BackoffMinutes = 3,
            RelaxEndpointPolicies = true
        });
        var resolver = new RateLimitPolicyResolver(options);
        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(Intelibill.Api.Controllers.AuthController).GetTypeInfo(),
            MethodInfo = typeof(Intelibill.Api.Controllers.AuthController).GetMethod(nameof(Intelibill.Api.Controllers.AuthController.LoginWithEmail))!
        };

        var policy = resolver.Resolve(CreateContext(actionDescriptor));

        Assert.Equal(100, policy.Limit);
        Assert.Equal(RateLimitSubject.Account, policy.Subject);
        Assert.True(policy.FailClosed);
    }

    [Fact]
    public void Resolve_WhenNoOverride_FailsOpenWithoutSubject()
    {
        var policy = CreateResolver().Resolve(CreateContext(new ActionDescriptor()));

        Assert.Equal(RateLimitSubject.None, policy.Subject);
        Assert.False(policy.FailClosed);
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
