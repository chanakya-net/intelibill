using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Item : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Uom { get; private set; } = string.Empty;
    public string Barcode { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public Guid? PreferredSupplierId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public Inventory? Inventory { get; private set; }
    public ICollection<InventoryBatch> Batches { get; private set; } = [];
    public ICollection<StockTransaction> StockTransactions { get; private set; } = [];

    private Item() { }

    public static Item Create(
        Guid shopId,
        string name,
        string? description,
        string uom,
        string barcode,
        bool isActive,
        Guid? preferredSupplierId,
        Guid createdBy)
    {
        return new Item
        {
            ShopId = shopId,
            Name = name.Trim(),
            Description = NormalizeOptional(description),
            Uom = uom.Trim(),
            Barcode = barcode.Trim(),
            IsActive = isActive,
            PreferredSupplierId = preferredSupplierId,
            CreatedBy = createdBy,
        };
    }

    public void Update(
        string name,
        string? description,
        string uom,
        string barcode,
        bool isActive,
        Guid? preferredSupplierId,
        Guid updatedBy)
    {
        Name = name.Trim();
        Description = NormalizeOptional(description);
        Uom = uom.Trim();
        Barcode = barcode.Trim();
        IsActive = isActive;
        PreferredSupplierId = preferredSupplierId;
        UpdatedBy = updatedBy;
    }

    public void MarkUpdatedBy(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}