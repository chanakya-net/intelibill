using Intelibill.Api.Hubs;
using Intelibill.Api.Services;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Api.Unit.Tests.Services;

public class SignalRShopUpdatesNotifierTests
{
    private readonly IHubContext<ShopUpdatesHub> _hubContext = Substitute.For<IHubContext<ShopUpdatesHub>>();
    private readonly IHubClients _hubClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _groupClients = Substitute.For<IClientProxy>();
    private readonly SignalRShopUpdatesNotifier _notifier;

    public SignalRShopUpdatesNotifierTests()
    {
        _hubContext.Clients.Returns(_hubClients);
        _hubClients.Group(Arg.Any<string>()).Returns(_groupClients);
        _notifier = new SignalRShopUpdatesNotifier(_hubContext);
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_SendsShopUpdatedEvent_WithCorrectGroup()
    {
        var shopId = Guid.NewGuid();
        var changedIds = new[] { Guid.NewGuid() };
        var occurredOn = DateTimeOffset.UtcNow;

        await _notifier.NotifyShopUpdateAsync("DiscountRuleChanged", shopId, changedIds, occurredOn);

        _hubClients.Received(1).Group(ShopUpdatesHub.ActiveShopGroupName(shopId));
        await _groupClients.Received(1).SendCoreAsync(
            "ShopUpdated",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_SendsCorrectPayloadShape()
    {
        var shopId = Guid.NewGuid();
        var changedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var occurredOn = DateTimeOffset.UtcNow;
        object?[]? capturedArgs = null;

        await _groupClients.SendCoreAsync(
            Arg.Any<string>(),
            Arg.Do<object?[]>(a => capturedArgs = a),
            Arg.Any<CancellationToken>());

        await _notifier.NotifyShopUpdateAsync("DiscountRuleChanged", shopId, changedIds, occurredOn);

        Assert.NotNull(capturedArgs);
        Assert.Single(capturedArgs);

        var payload = capturedArgs[0]!;
        var type = payload.GetType();
        Assert.Equal("DiscountRuleChanged", type.GetProperty("eventType")!.GetValue(payload));
        Assert.Equal(shopId, type.GetProperty("shopId")!.GetValue(payload));
        var changedIdsValue = (IReadOnlyCollection<Guid>)type.GetProperty("changedIds")!.GetValue(payload)!;
        Assert.Equal(changedIds, changedIdsValue);
        Assert.Equal(occurredOn, type.GetProperty("occurredOn")!.GetValue(payload));
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var shopId = Guid.NewGuid();
        var changedIds = new[] { Guid.NewGuid() };
        var occurredOn = DateTimeOffset.UtcNow;

        await _notifier.NotifyShopUpdateAsync("DiscountRuleChanged", shopId, changedIds, occurredOn, cts.Token);

        await _groupClients.Received(1).SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object?[]>(),
            cts.Token);
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_WhenSendAsyncThrows_ExceptionPropagates()
    {
        _groupClients
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Hub unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _notifier.NotifyShopUpdateAsync(
                "DiscountRuleChanged",
                Guid.NewGuid(),
                new[] { Guid.NewGuid() },
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task NotifyShopUpdateAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _groupClients
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _notifier.NotifyShopUpdateAsync(
                "DiscountRuleChanged",
                Guid.NewGuid(),
                new[] { Guid.NewGuid() },
                DateTimeOffset.UtcNow,
                cts.Token));
    }
}
