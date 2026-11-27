using Intelibill.Application.Common.Licensing;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

[LicenceAction(LicenceAction.InternalService)]
internal sealed partial class ShopAffiliationDeliveryWorker(IServiceScopeFactory scopes,
    IOptions<MerchantDeliveryPollingOptions> polling, ILogger<ShopAffiliationDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(polling.Value.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ShopAffiliationDelivery>().DrainAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested &&
                exception is NpgsqlException or HttpRequestException or TimeoutException or OperationCanceledException
                    or JsonException or IOException or CryptographicException or RequestFailedException or AuthenticationFailedException)
            {
                LogDelayed(logger, exception.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shop affiliation delivery will retry ({Cause}).")]
    private static partial void LogDelayed(ILogger logger, string cause);
}
