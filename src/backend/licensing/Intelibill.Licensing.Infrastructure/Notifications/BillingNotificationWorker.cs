using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed partial class BillingNotificationWorker(
    IServiceScopeFactory scopes,
    IOptions<BillingEmailOptions> settings,
    IOptions<AdminMailOptions> smtp,
    IHostEnvironment environment,
    ILogger<BillingNotificationWorker> logger) : BackgroundService
{
    internal const string LeaseClock = "billing-email-lease-clock";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.RunInBackground || !settings.Value.CanCapture(smtp.Value, environment))
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<IBillingEmailDispatcher>().DeliverOneAsync(stoppingToken))
                    continue;
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassDeferred(logger);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Billing email pass deferred.")]
    private static partial void LogPassDeferred(ILogger logger);
}
