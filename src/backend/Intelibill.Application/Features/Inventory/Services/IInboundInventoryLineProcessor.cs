using ErrorOr;

namespace Intelibill.Application.Features.Inventory.Services;

public interface IInboundInventoryLineProcessor
{
    Task<ErrorOr<InboundInventoryLineResult>> ProcessAsync(
        Guid shopId,
        InboundInventoryLineInput input,
        Guid actorUserId,
        ItemResolutionContext itemResolutionContext,
        InventoryUpdateContext inventoryUpdateContext,
        CancellationToken cancellationToken);
}
