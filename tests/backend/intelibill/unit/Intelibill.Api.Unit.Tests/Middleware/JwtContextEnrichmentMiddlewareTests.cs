using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Intelibill.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace Intelibill.Api.Unit.Tests.Middleware;

public class JwtContextEnrichmentMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenUserIsNotAuthenticated_CallsNextWithoutEnrichment()
    {
        var nextCalled = false;
        var middleware = new JwtContextEnrichmentMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // unauthenticated

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenUserAuthenticatedWithSubClaim_CallsNext()
    {
        var nextCalled = false;
        var middleware = new JwtContextEnrichmentMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = BuildContext(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("active_shop_id", Guid.NewGuid().ToString()));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenUserAuthenticatedWithNameIdentifier_CallsNext()
    {
        var nextCalled = false;
        var middleware = new JwtContextEnrichmentMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = BuildContext(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenUserAuthenticatedWithAllClaims_CallsNext()
    {
        var nextCalled = false;
        var middleware = new JwtContextEnrichmentMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = BuildContext(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("active_shop_id", Guid.NewGuid().ToString()),
            new Claim("active_shop_role", "Owner"),
            new Claim("tenant_id", Guid.NewGuid().ToString()));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_ExceptionPropagatesAfterCleanup()
    {
        var middleware = new JwtContextEnrichmentMiddleware(_ =>
            throw new InvalidOperationException("test failure"));

        var context = BuildContext(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("active_shop_id", Guid.NewGuid().ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_WhenUserAuthenticatedWithShopIdFallbackClaim_CallsNext()
    {
        var nextCalled = false;
        var middleware = new JwtContextEnrichmentMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Use "shop_id" fallback claim instead of "active_shop_id"
        var context = BuildContext(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("shop_id", Guid.NewGuid().ToString()));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static DefaultHttpContext BuildContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "test");
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }
}
