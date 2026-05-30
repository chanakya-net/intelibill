using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Discounts.Events;
using Intelibill.Domain.Events;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Application.Unit.Tests.Features.Discounts.Events;

public class DiscountRuleChangedDomainEventHandlerTests
{
    private readonly IShopUpdatesNotifier _notifier = Substitute.For<IShopUpdatesNotifier>();

    [Fact]
    public async Task HandleAsync_ValidEvent_CallsNotifierWithCorrectPayload()
    {
        var shopId = Guid.NewGuid();
        var changedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var @event = new DiscountRuleChangedDomainEvent(shopId, changedIds);

        var handler = new DiscountRuleChangedDomainHandler(_notifier);
        await handler.HandleAsync(@event, CancellationToken.None);

        await _notifier.Received(1).NotifyShopUpdateAsync(
            "DiscountRuleChanged",
            shopId,
            changedIds,
            @event.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidEvent_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        var @event = new DiscountRuleChangedDomainEvent(Guid.NewGuid(), new[] { Guid.NewGuid() });

        var handler = new DiscountRuleChangedDomainHandler(_notifier);
        await handler.HandleAsync(@event, cts.Token);

        await _notifier.Received(1).NotifyShopUpdateAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<DateTimeOffset>(),
            cts.Token);
    }

    [Fact]
    public async Task HandleAsync_NotifierThrows_ExceptionPropagates()
    {
        var @event = new DiscountRuleChangedDomainEvent(Guid.NewGuid(), new[] { Guid.NewGuid() });
        _notifier
            .NotifyShopUpdateAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));

        var handler = new DiscountRuleChangedDomainHandler(_notifier);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(@event, CancellationToken.None));
    }
}
