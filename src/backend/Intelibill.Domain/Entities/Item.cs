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
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public string? HsnCode { get; private set; }
    public decimal DefaultTaxRatePercent { get; private set; }
    public bool DefaultTaxIncluded { get; private set; }

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
        Guid createdBy,
        string? hsnCode = null,
        decimal defaultTaxRatePercent = 0m,
        bool defaultTaxIncluded = false)
    {
        var item = new Item
        {
            ShopId = shopId,
            Name = name.Trim(),
            Description = NormalizeOptional(description),
            Uom = uom.Trim(),
            Barcode = barcode.Trim(),
            IsActive = isActive,
            CreatedBy = createdBy,
            HsnCode = NormalizeOptional(hsnCode),
            DefaultTaxRatePercent = defaultTaxRatePercent,
            DefaultTaxIncluded = defaultTaxIncluded,
        };

        item.AddDomainEvent(new Events.ItemCreatedDomainEvent(item.Id, item.Barcode, item.Name, item.ShopId));

        return item;
    }

    public void Update(
        string name,
        string? description,
        string uom,
        string barcode,
        bool isActive,
        Guid updatedBy,
        string? hsnCode = null,
        decimal defaultTaxRatePercent = 0m,
        bool defaultTaxIncluded = false)
    {
        Name = name.Trim();
        Description = NormalizeOptional(description);
        Uom = uom.Trim();
        Barcode = barcode.Trim();
        IsActive = isActive;
        UpdatedBy = updatedBy;
        UpdateTaxDefaults(hsnCode, defaultTaxRatePercent, defaultTaxIncluded);
    }

    public void MarkUpdatedBy(Guid updatedBy)
    {
        UpdatedBy = updatedBy;
    }

    public void UpdateHsnCode(string? hsnCode)
    {
        HsnCode = NormalizeOptional(hsnCode);
    }

    public void UpdateTaxDefaults(string? hsnCode, decimal defaultTaxRatePercent, bool defaultTaxIncluded = false)
    {
        HsnCode = NormalizeOptional(hsnCode);
        DefaultTaxRatePercent = defaultTaxRatePercent;
        DefaultTaxIncluded = defaultTaxIncluded;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
