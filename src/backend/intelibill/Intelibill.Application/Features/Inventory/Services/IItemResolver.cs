using ErrorOr;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Inventory.Services;

public sealed class ItemResolutionContext
{
    internal Dictionary<string, Item> ByBarcode { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Item> ByName { get; } = new(StringComparer.Ordinal);
}

public interface IItemResolver
{
    Task<ErrorOr<Item>> ResolveAsync(
        Guid shopId,
        string name,
        string barcode,
        string? description,
        string uom,
        Guid actorUserId,
        ItemResolutionContext context,
        CancellationToken cancellationToken);
}
