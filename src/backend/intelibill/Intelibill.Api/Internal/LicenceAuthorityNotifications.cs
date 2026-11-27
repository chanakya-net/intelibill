using System.Threading.Channels;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;

namespace Intelibill.Api.Internal;

/// <summary>Advisory refresh notices run separately from signed projection acknowledgements.</summary>
[LicenceAction(LicenceAction.InternalService)]
internal sealed partial class LicenceAuthorityNotifications(
    IServiceScopeFactory services, TimeProvider clock, ILogger<LicenceAuthorityNotifications> logger) : BackgroundService
{
    private readonly Channel<Guid> _shops = Channel.CreateBounded<Guid>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public void Schedule(Guid shopId) => _shops.Writer.TryWrite(shopId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var shopId in _shops.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IShopUpdatesNotifier>()
                    .NotifyShopUpdateAsync("LicenceAuthorityChanged", shopId, [], clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogRefreshUnavailable(logger, error);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The shop licence refresh notification could not be sent.")]
    private static partial void LogRefreshUnavailable(ILogger logger, Exception error);
}
