using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Api.Options;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class RateLimitKeyHasherTests
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public void WithoutKey_EveryInstanceHashesTheSameWay()
    {
        var a = Create(new RateLimitingOptions()).Hash("email:a@example.com");
        var b = Create(new RateLimitingOptions()).Hash("email:a@example.com");

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", Assert.Single(a));
    }

    [Fact]
    public void WithKey_DiffersFromUnkeyedAndIsStableAcrossInstances()
    {
        var unkeyed = Create(new RateLimitingOptions()).Hash("email:a@example.com");
        var keyedA = Create(new RateLimitingOptions { BucketKey = Key }).Hash("email:a@example.com");
        var keyedB = Create(new RateLimitingOptions { BucketKey = Key }).Hash("email:a@example.com");

        Assert.Equal(keyedA, keyedB);
        Assert.NotEqual(unkeyed, keyedA);
    }

    private static RateLimitKeyHasher Create(RateLimitingOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options));
}
