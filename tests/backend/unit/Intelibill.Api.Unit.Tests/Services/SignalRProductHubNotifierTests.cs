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
public class SignalRProductHubNotifierTests
{
    private readonly IAuthorizedRealtimeDispatcher<ProductHub> _dispatcher =
        Substitute.For<IAuthorizedRealtimeDispatcher<ProductHub>>();

    private readonly SignalRProductHubNotifier _notifier;

    public SignalRProductHubNotifierTests() => _notifier = new SignalRProductHubNotifier(_dispatcher);

    [Fact]
    public async Task NotifyProductCreatedAsync_DispatchesProductAddedForTheShop()
    {
        var itemId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        await _notifier.NotifyProductCreatedAsync(itemId, "BAR001", "Rice", shopId);

        await _dispatcher.Received(1).SendShopAsync(
            shopId,
            "ProductAdded",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_SendsCorrectPayloadShape()
    {
        var itemId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        object? captured = null;
        await _dispatcher.SendShopAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Do<object>(p => captured = p),
            Arg.Any<CancellationToken>());

        await _notifier.NotifyProductCreatedAsync(itemId, "BAR001", "Rice", shopId);

        Assert.NotNull(captured);
        var type = captured.GetType();
        Assert.Equal(itemId, type.GetProperty("itemId")!.GetValue(captured));
        Assert.Equal("BAR001", type.GetProperty("barcode")!.GetValue(captured));
        Assert.Equal("Rice", type.GetProperty("name")!.GetValue(captured));
        Assert.Equal(shopId, type.GetProperty("shopId")!.GetValue(captured));
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();

        await _notifier.NotifyProductCreatedAsync(Guid.NewGuid(), "BAR001", "Rice", Guid.NewGuid(), cts.Token);

        await _dispatcher.Received(1).SendShopAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<object>(),
            cts.Token);
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_WhenDispatchThrows_ExceptionPropagates()
    {
        _dispatcher
            .SendShopAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Hub unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _notifier.NotifyProductCreatedAsync(Guid.NewGuid(), "B1", "Item", Guid.NewGuid()));
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _dispatcher
            .SendShopAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _notifier.NotifyProductCreatedAsync(Guid.NewGuid(), "B1", "Item", Guid.NewGuid(), cts.Token));
    }
}
