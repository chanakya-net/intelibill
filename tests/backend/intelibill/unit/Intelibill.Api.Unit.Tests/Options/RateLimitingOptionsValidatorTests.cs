using Intelibill.Api.Options;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Api.Unit.Tests.Options;

/// <summary>
/// Relaxing the per-endpoint rate limits is a development affordance. Mirrors
/// <c>EmailOptionsValidator</c>: the setting is refused outright anywhere else, so
/// a value copied into a deployed configuration stops that revision from starting
/// rather than quietly widening the limits that protect the auth endpoints.
/// </summary>
public sealed class RateLimitingOptionsValidatorTests
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Validate_WhenRelaxedOutsideDevelopment_Fails(string environmentName)
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment(environmentName));

        var result = validator.Validate(name: null, new RateLimitingOptions { RelaxEndpointPolicies = true });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("RateLimiting:RelaxEndpointPolicies", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Validate_WhenRelaxedInALocalEnvironment_Succeeds(string environmentName)
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment(environmentName));

        var result = validator.Validate(name: null, new RateLimitingOptions { RelaxEndpointPolicies = true });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenNotRelaxed_SucceedsInEveryEnvironment()
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment("Production"));

        var result = validator.Validate(name: null, new RateLimitingOptions { BucketKey = Key });

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// A missing key is not a reason to refuse to start: buckets are then hashed without one,
    /// which every replica still agrees on, and the hasher warns. Refusing would take a deployed
    /// revision down until a new secret is provisioned.
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Validate_WhenBucketKeyMissing_Succeeds(string environmentName)
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment(environmentName));

        var result = validator.Validate(name: null, new RateLimitingOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public void Validate_WhenBucketKeyMalformed_Fails(string key)
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment("Development"));

        var current = validator.Validate(name: null, new RateLimitingOptions { BucketKey = key });
        var previous = validator.Validate(name: null, new RateLimitingOptions { BucketKey = Key, PreviousBucketKey = key });

        Assert.True(current.Failed);
        Assert.True(previous.Failed);
        Assert.Contains(previous.Failures!, failure => failure.Contains("RateLimiting:PreviousBucketKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenRotatingAwayFromUnkeyedHashing_Succeeds()
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment("Production"));

        var result = validator.Validate(name: null, new RateLimitingOptions { BucketKey = Key, PreviousBucketKey = "unkeyed" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenSubjectLimitsAreNotPositive_Fails()
    {
        var validator = new RateLimitingOptionsValidator(new StubEnvironment("Development"));

        var result = validator.Validate(name: null, new RateLimitingOptions
        {
            Account = new SubjectBudgetOptions { Limit = 0, PeriodInMinutes = 15 },
            Destination = new SubjectBudgetOptions { Limit = 3, PeriodInMinutes = 0, DailyLimit = -1 },
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("RateLimiting:Account:Limit", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("RateLimiting:Destination:PeriodInMinutes", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("RateLimiting:Destination:DailyLimit", StringComparison.Ordinal));
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
