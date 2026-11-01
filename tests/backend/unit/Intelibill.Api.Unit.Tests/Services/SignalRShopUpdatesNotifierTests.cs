using Intelibill.Api.Hubs;
using Intelibill.Api.Services;
using Intelibill.Api.Services.Realtime;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Api.Unit.Tests.Services;

/// <summary>
/// The notifier's whole job is now the payload and the event name. Who may receive it is the
/// dispatcher's decision, which <see cref="AuthorizedRealtimeDispatcherTests"/> covers.
/// </summary>
public class SignalRShopUpdatesNotifierTests
{
    private readonly IAuthorizedRealtimeDispatcher<ShopUpdatesHub> _dispatcher =
        Substitute.For<IAuthorizedRealtimeDispatcher<ShopUpdatesHub>>();

    private readonly SignalRShopUpdatesNotifier _notifier;

    public SignalRShopUpdatesNotifierTests() => _notifier = new SignalRShopUpdatesNotifier(_dispatcher);

    [Fact]
    public async Task NotifyShopUpdateAsync_DispatchesShopUpdatedForTheShop()
    {
        var shopId = Guid.NewGuid();

        await _notifier.NotifyShopUpdateAsync(
            "DiscountRuleChanged",
            shopId,
            [Guid.NewGuid()],
            DateTimeOffset.UtcNow);

        await _dispatcher.Received(1).SendShopAsync(
            shopId,
            "ShopUpdated",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_SendsCorrectPayloadShape()
    {
        var shopId = Guid.NewGuid();
        var changedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var occurredOn = DateTimeOffset.UtcNow;
        object? captured = null;
        await _dispatcher.SendShopAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Do<object>(p => captured = p),
            Arg.Any<CancellationToken>());

        await _notifier.NotifyShopUpdateAsync("DiscountRuleChanged", shopId, changedIds, occurredOn);

        Assert.NotNull(captured);
        var type = captured.GetType();
        Assert.Equal("DiscountRuleChanged", type.GetProperty("eventType")!.GetValue(captured));
        Assert.Equal(shopId, type.GetProperty("shopId")!.GetValue(captured));
        var changedIdsValue = (IReadOnlyCollection<Guid>)type.GetProperty("changedIds")!.GetValue(captured)!;
        Assert.Equal(changedIds, changedIdsValue);
        Assert.Equal(occurredOn, type.GetProperty("occurredOn")!.GetValue(captured));
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();

        await _notifier.NotifyShopUpdateAsync(
            "DiscountRuleChanged",
            Guid.NewGuid(),
            [Guid.NewGuid()],
            DateTimeOffset.UtcNow,
            cts.Token);

        await _dispatcher.Received(1).SendShopAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<object>(),
            cts.Token);
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_WhenDispatchThrows_ExceptionPropagates()
    {
        _dispatcher
            .SendShopAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Hub unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _notifier.NotifyShopUpdateAsync(
                "DiscountRuleChanged",
                Guid.NewGuid(),
                [Guid.NewGuid()],
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _dispatcher
            .SendShopAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _notifier.NotifyShopUpdateAsync(
                "DiscountRuleChanged",
                Guid.NewGuid(),
                [Guid.NewGuid()],
                DateTimeOffset.UtcNow,
                cts.Token));
    }
}
