using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.Events;
using Intelibill.Domain.Events;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Application.Unit.Tests.Features.Items.Events;

public class ItemCreatedDomainHandlerTests
{
    private readonly IProductHubNotifier _hubNotifier = Substitute.For<IProductHubNotifier>();

    [Fact]
    public async Task HandleAsync_ValidEvent_CallsHubNotifierWithCorrectPayload()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var @event = new ItemCreatedDomainEvent(itemId, "BAR001", "Rice", shopId);

        var handler = new ItemCreatedDomainHandler(_hubNotifier);
        await handler.HandleAsync(@event, CancellationToken.None);

        await _hubNotifier.Received(1).NotifyProductCreatedAsync(
            itemId,
            "BAR001",
            "Rice",
            shopId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidEvent_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        var @event = new ItemCreatedDomainEvent(Guid.NewGuid(), "BAR002", "Wheat", Guid.NewGuid());

        var handler = new ItemCreatedDomainHandler(_hubNotifier);
        await handler.HandleAsync(@event, cts.Token);

        await _hubNotifier.Received(1).NotifyProductCreatedAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            cts.Token);
    }

    [Fact]
    public async Task HandleAsync_HubNotifierThrows_ExceptionPropagates()
    {
        var @event = new ItemCreatedDomainEvent(Guid.NewGuid(), "BAR003", "Salt", Guid.NewGuid());
        _hubNotifier
            .NotifyProductCreatedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));

        var handler = new ItemCreatedDomainHandler(_hubNotifier);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(@event, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DoesNotCallHubNotifier_WhenCancelledBeforeCall()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _hubNotifier
            .NotifyProductCreatedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var @event = new ItemCreatedDomainEvent(Guid.NewGuid(), "BAR004", "Sugar", Guid.NewGuid());
        var handler = new ItemCreatedDomainHandler(_hubNotifier);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.HandleAsync(@event, cts.Token));
    }
}
