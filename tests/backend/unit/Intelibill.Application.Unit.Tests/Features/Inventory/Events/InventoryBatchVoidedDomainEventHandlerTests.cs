using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Inventory.Events;
using Intelibill.Domain.Events;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Events;

public class InventoryBatchVoidedDomainEventHandlerTests
{
    private readonly IShopUpdatesNotifier _notifier = Substitute.For<IShopUpdatesNotifier>();

    [Fact]
    public async Task HandleAsync_ValidEvent_NotifiesShopUpdate()
    {
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var @event = new InventoryBatchVoidedDomainEvent(shopId, itemId, Guid.NewGuid());

        var handler = new InventoryBatchVoidedDomainHandler(_notifier);
        await handler.HandleAsync(@event, CancellationToken.None);

        await _notifier.Received(1).NotifyShopUpdateAsync(
            "InventoryBatchVoided",
            shopId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(itemId)),
            @event.OccurredOn,
            Arg.Any<CancellationToken>());
    }
}
