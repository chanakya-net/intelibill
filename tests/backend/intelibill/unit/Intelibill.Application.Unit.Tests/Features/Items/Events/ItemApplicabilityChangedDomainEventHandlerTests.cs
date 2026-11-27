using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.Events;
using Intelibill.Domain.Events;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Events;

public class ItemApplicabilityChangedDomainEventHandlerTests
{
    private readonly IShopUpdatesNotifier _notifier = Substitute.For<IShopUpdatesNotifier>();

    [Fact]
    public async Task HandleAsync_ValidEvent_NotifiesShopUpdate()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var @event = new ItemApplicabilityChangedDomainEvent(shopId, itemId);

        var handler = new ItemApplicabilityChangedDomainHandler(_notifier);
        await handler.HandleAsync(@event, CancellationToken.None);

        await _notifier.Received(1).NotifyShopUpdateAsync(
            "ItemApplicabilityChanged",
            shopId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(itemId)),
            @event.OccurredOn,
            Arg.Any<CancellationToken>());
    }
}
