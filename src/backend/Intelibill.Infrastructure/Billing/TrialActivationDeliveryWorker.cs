using Intelibill.Application.Common.Licensing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Intelibill.Infrastructure.Billing;

[LicenceAction(LicenceAction.InternalService)]
internal sealed class TrialActivationDeliveryWorker(IServiceScopeFactory scopes,
    ILogger<TrialActivationDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TrialActivationDelivery>().DrainAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested && TrialActivationDelivery.IsRetryable(error))
            {
                TrialActivationDelivery.LogRetry(logger, error.GetType().Name);
            }
        }
    }
}
