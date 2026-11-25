using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class RefundAttemptWorker(IServiceScopeFactory scopes, IOptions<PaymentOptions> options,
    IHostEnvironment environment, ILogger<RefundAttemptWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ReconcileInBackground || !(environment.IsDevelopment() || environment.IsEnvironment("Testing")))
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await PumpAsync(scopes, stoppingToken); }
            catch (Exception e) when (PaymentStorageFailure.IsUnavailable(e))
            {
                stoppingToken.ThrowIfCancellationRequested();
                StorageUnavailable(logger, e.GetType().Name);
            }
        }
    }

    internal static async Task PumpAsync(IServiceScopeFactory scopes, CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<IRefundReconciliation>().RunOneAsync(ct)) return;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refund reconciliation storage unavailable ({Cause}).")]
    private static partial void StorageUnavailable(ILogger logger, string cause);
}
