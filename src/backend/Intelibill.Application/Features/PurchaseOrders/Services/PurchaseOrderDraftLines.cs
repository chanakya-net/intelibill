using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Services;

internal static class PurchaseOrderDraftLines
{
    public static async Task<ErrorOr<List<(Guid ItemId, string Description, int ExpectedQuantity, decimal UnitCost)>>> ValidateAsync(
        Guid shopId, IEnumerable<(Guid ItemId, string Description, int ExpectedQuantity, decimal UnitCost)> inputs,
        IItemRepository itemRepository, CancellationToken ct)
    {
        var itemIds = new HashSet<Guid>();
        var lines = new List<(Guid, string, int, decimal)>();
        foreach (var (itemId, description, expectedQuantity, unitCost) in inputs)
        {
            if (itemId == Guid.Empty) return Errors.PurchaseOrder.LineItemRequired;
            if (string.IsNullOrWhiteSpace(description)) return Errors.PurchaseOrder.LineDescriptionRequired;
            if (!itemIds.Add(itemId)) return Errors.PurchaseOrder.DuplicateItem;
            if (expectedQuantity <= 0) return Errors.PurchaseOrder.InvalidLineQuantity;
            if (unitCost < 0) return Errors.PurchaseOrder.InvalidLineUnitCost;

            lines.Add((itemId, description.Trim(), expectedQuantity, unitCost));
        }

        if (itemIds.Count == 0) return lines;

        var items = await itemRepository.GetByIdsAsync(shopId, itemIds.ToList(), ct);
        return items.Count != itemIds.Count || items.Any(item => item.ShopId != shopId) ||
            !itemIds.SetEquals(items.Select(item => item.Id))
            ? Errors.PurchaseOrder.LineItemNotFound
            : lines;
    }
}
