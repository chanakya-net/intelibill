using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Payments.Razorpay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class GatewayInboxWorker(IServiceScopeFactory scopes, IOptions<PaymentOptions> options,
    ILogger<GatewayInboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ReconcileInBackground || !Enabled())
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PumpAsync(stoppingToken);
            }
            catch (Exception error) when (error is NpgsqlException or TimeoutException)
            {
                stoppingToken.ThrowIfCancellationRequested();
                StorageUnavailable(logger);
            }
        }
    }

    private bool Enabled()
    {
        using var scope = scopes.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();
        return resolver.ForWebhooks(RazorpayConfiguration.ProviderId).Count != 0 ||
            resolver.ForWebhooks(DevelopmentPaymentGateway.LocalPin.ProviderId).Count != 0;
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        for (var index = 0; index < PendingPaymentAttemptWorker.BatchSize; index++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var clock = services.GetRequiredKeyedService<TimeProvider>(PendingPaymentAttemptWorker.ClockKey);
            var store = services.GetRequiredService<IGatewayInboxStore>();
            if (await store.ClaimAsync(clock.GetUtcNow(), ct) is not { } lease)
                return;
            var result = await services.GetRequiredService<ProcessGatewayInboxHandler>().HandleAsync(lease, ct);
            if (result?.Failure is { } failure && (result.DeadLetter || lease.Attempts == 12))
                DeadLettered(logger, lease.InboxId, failure);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook processing storage unavailable.")]
    private static partial void StorageUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook {InboxId} dead-lettered ({Reason}).")]
    private static partial void DeadLettered(ILogger logger, Guid inboxId, GatewayInboxFailure reason);
}
