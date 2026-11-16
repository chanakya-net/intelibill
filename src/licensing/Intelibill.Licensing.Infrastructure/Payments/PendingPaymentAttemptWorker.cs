using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PendingPaymentAttemptWorker(IServiceScopeFactory scopes, IHostEnvironment environment,
    IOptions<PaymentOptions> options, ILogger<PendingPaymentAttemptWorker> logger) : BackgroundService
{
    internal const string ClockKey = "pending-payment-lease";
    internal const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ReconcileInBackground || !PaymentGatewayResolver.SimulatorEnabled(options.Value, environment))
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PumpAsync(scopes, stoppingToken);
            }
            catch (Exception error) when (error is NpgsqlException or TimeoutException)
            {
                stoppingToken.ThrowIfCancellationRequested();
                LogUnavailable(logger, error.GetType().Name);
            }
        }
    }

    internal static async Task PumpAsync(IServiceScopeFactory scopes, CancellationToken ct)
    {
        for (var i = 0; i < BatchSize; i++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var clock = services.GetRequiredKeyedService<TimeProvider>(ClockKey);
            var pending = services.GetRequiredService<IPendingPaymentAttemptStore>();
            if (await pending.ClaimAsync(clock.GetUtcNow(), ct) is not { } lease)
                return;
            await services.GetRequiredService<ReconcilePaymentHandler>().HandleLeaseAsync(lease, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment reconciliation storage unavailable ({Cause}).")]
    private static partial void LogUnavailable(ILogger logger, string cause);
}
