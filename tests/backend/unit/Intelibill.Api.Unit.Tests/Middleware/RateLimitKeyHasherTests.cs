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

    [Fact]
    public void UnkeyedPrevious_KeepsCountingTheUnkeyedBucketsDuringTheFirstRotation()
    {
        var unkeyed = Create(new RateLimitingOptions()).Hash("email:a@example.com").Single();
        var keyed = Create(new RateLimitingOptions { BucketKey = Key }).Hash("email:a@example.com").Single();

        var rotating = Create(new RateLimitingOptions { BucketKey = Key, PreviousBucketKey = RateLimitKeyHasher.UnkeyedSentinel })
            .Hash("email:a@example.com");

        Assert.Equal([keyed, unkeyed], rotating);
    }

    [Theory]
    [InlineData("Production", 1)]
    [InlineData("Development", 0)]
    public void WithoutKey_ReportsAnErrorOutsideLocalEnvironments(string environmentName, int expectedErrors)
    {
        var logger = new RecordingLogger();

        _ = new RateLimitKeyHasher(
            Microsoft.Extensions.Options.Options.Create(new RateLimitingOptions()),
            new StubEnvironment(environmentName),
            logger);

        Assert.Equal(expectedErrors, logger.Levels.Count(level => level == Microsoft.Extensions.Logging.LogLevel.Error));
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<RateLimitKeyHasher>
    {
        public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    private sealed class StubEnvironment(string environmentName) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static RateLimitKeyHasher Create(RateLimitingOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options));
}
