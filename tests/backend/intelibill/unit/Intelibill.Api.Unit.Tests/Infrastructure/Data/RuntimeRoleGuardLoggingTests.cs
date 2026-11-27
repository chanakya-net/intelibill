using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Data;

public sealed class RuntimeRoleGuardLoggingTests
{
    [Fact]
    public async Task ConnectRetry_LogsTheFailureKindNotItsMessage()
    {
        // Nothing listens on port 1, so the first attempt fails at once with a provider message
        // naming the host — the kind of text that, for other failures, names users and servers.
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=probe;Password=probe;Timeout=2");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var logger = new FirstWarningLogger(stop);
        var guard = new RuntimeRoleGuard(
            dataSource,
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { RequireRestrictedRuntimeRole = true }),
            new HostingEnvironment { EnvironmentName = Environments.Production },
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.StartAsync(stop.Token));

        var warning = Assert.Single(logger.Warnings);
        Assert.DoesNotContain("127.0.0.1", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to connect", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(NpgsqlException), warning, StringComparison.Ordinal);
    }

    /// <summary>Records warnings and stops the guard at the first one, before its back-off.</summary>
    private sealed class FirstWarningLogger(CancellationTokenSource stop) : ILogger<RuntimeRoleGuard>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning)
                return;
            Warnings.Add(formatter(state, exception));
            stop.Cancel();
        }
    }
}
