using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

internal sealed class ItemResolver(
    IItemRepository itemRepository) : IItemResolver
{
    public async Task<ErrorOr<Item>> ResolveAsync(
        Guid shopId,
        string name,
        string barcode,
        string? description,
        string uom,
        Guid actorUserId,
        ItemResolutionContext context,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        var normalizedBarcode = barcode.Trim();

        var cacheBarcodeKey = BuildCacheKey(shopId, normalizedBarcode);
        var cacheNameKey = BuildCacheKey(shopId, normalizedName);

        if (!context.ByBarcode.TryGetValue(cacheBarcodeKey, out var itemByBarcode))
        {
            itemByBarcode = await itemRepository.GetByBarcodeAsync(shopId, normalizedBarcode, cancellationToken);
            if (itemByBarcode is not null) context.ByBarcode[cacheBarcodeKey] = itemByBarcode;
        }

        if (!context.ByName.TryGetValue(cacheNameKey, out var itemByName))
        {
            itemByName = await itemRepository.GetByNameAsync(shopId, normalizedName, cancellationToken);
            if (itemByName is not null) context.ByName[cacheNameKey] = itemByName;
        }

        if (itemByBarcode is not null && itemByName is not null && itemByBarcode.Id != itemByName.Id)
            return Errors.Inventory.ItemIdentityConflict;

        if (itemByBarcode is not null)
        {
            if (!string.Equals(itemByBarcode.Name, normalizedName, StringComparison.Ordinal))
                return Errors.Inventory.ItemNameBarcodeMismatch;

            return itemByBarcode;
        }

        if (itemByName is not null)
        {
            if (!string.Equals(itemByName.Barcode, normalizedBarcode, StringComparison.Ordinal))
                return Errors.Inventory.ItemNameBarcodeMismatch;

            return itemByName;
        }

        var item = Item.Create(
            shopId,
            normalizedName,
            description?.Trim(),
            uom.Trim(),
            normalizedBarcode,
            isActive: true,
            createdBy: actorUserId);

        await itemRepository.AddAsync(item, cancellationToken);
        context.ByBarcode[cacheBarcodeKey] = item;
        context.ByName[cacheNameKey] = item;

        return item;
    }

    private static string BuildCacheKey(Guid shopId, string value) => $"{shopId:N}:{value}";
}
