using Intelibill.Api.Hubs;
using Intelibill.Api.Services;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Api.Unit.Tests.Services;

public class SignalRProductHubNotifierTests
{
    private readonly IHubContext<ProductHub> _hubContext = Substitute.For<IHubContext<ProductHub>>();
    private readonly IHubClients _hubClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _groupClients = Substitute.For<IClientProxy>();
    private readonly SignalRProductHubNotifier _notifier;

    public SignalRProductHubNotifierTests()
    {
        _hubContext.Clients.Returns(_hubClients);
        _hubClients.Group(Arg.Any<string>()).Returns(_groupClients);
        _notifier = new SignalRProductHubNotifier(_hubContext);
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_SendsProductAddedEvent_ToTheActiveShopGroupOnly()
    {
        var itemId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        await _notifier.NotifyProductCreatedAsync(itemId, "BAR001", "Rice", shopId);

        _hubClients.Received(1).Group(ProductHub.ActiveShopGroupName(shopId));
        await _groupClients.Received(1).SendCoreAsync(
            "ProductAdded",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_SendsCorrectPayloadShape()
    {
        var itemId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        object?[]? capturedArgs = null;

        await _groupClients
            .SendCoreAsync(
                Arg.Any<string>(),
                Arg.Do<object?[]>(a => capturedArgs = a),
                Arg.Any<CancellationToken>());

        await _notifier.NotifyProductCreatedAsync(itemId, "BAR001", "Rice", shopId);

        Assert.NotNull(capturedArgs);
        Assert.Single(capturedArgs);

        var payload = capturedArgs[0]!;
        var type = payload.GetType();
        Assert.Equal(itemId, type.GetProperty("itemId")!.GetValue(payload));
        Assert.Equal("BAR001", type.GetProperty("barcode")!.GetValue(payload));
        Assert.Equal("Rice", type.GetProperty("name")!.GetValue(payload));
        Assert.Equal(shopId, type.GetProperty("shopId")!.GetValue(payload));
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var itemId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        await _notifier.NotifyProductCreatedAsync(itemId, "BAR001", "Rice", shopId, cts.Token);

        await _groupClients.Received(1).SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object?[]>(),
            cts.Token);
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_WhenSendAsyncThrows_ExceptionPropagates()
    {
        _groupClients
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Hub unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _notifier.NotifyProductCreatedAsync(Guid.NewGuid(), "B1", "Item", Guid.NewGuid()));
    }

    [Fact]
    public async Task NotifyProductCreatedAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _groupClients
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _notifier.NotifyProductCreatedAsync(Guid.NewGuid(), "B1", "Item", Guid.NewGuid(), cts.Token));
    }
}
