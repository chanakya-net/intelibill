using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Inventory.Events;
using Intelibill.Domain.Events;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Events;

public class InventoryBatchPricingChangedDomainEventHandlerTests
{
    private readonly IShopUpdatesNotifier _notifier = Substitute.For<IShopUpdatesNotifier>();

    [Fact]
    public async Task HandleAsync_ValidEvent_NotifiesShopUpdate()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var @event = new InventoryBatchPricingChangedDomainEvent(shopId, itemId, Guid.NewGuid());

        var handler = new InventoryBatchPricingChangedDomainHandler(_notifier);
        await handler.HandleAsync(@event, CancellationToken.None);

        await _notifier.Received(1).NotifyShopUpdateAsync(
            "InventoryBatchPricingChanged",
            shopId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(itemId)),
            @event.OccurredOn,
            Arg.Any<CancellationToken>());
    }
}
