using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class Item : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Uom { get; private set; } = string.Empty;
    public string? UnitCode { get; private set; }
    public int QuantityScale { get; private set; }
    public bool UnitReviewRequired { get; private set; }
    public string DocumentUnit => QuantityUnits.DocumentUnitFor(UnitCode, QuantityScale);
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
        bool defaultTaxIncluded = false,
        int? quantityScale = null)
    {
        var catalog = QuantityUnits.Resolve(uom);
        if (catalog is null && quantityScale is < 0 or > QuantityUnits.MaxScale)
            throw new ArgumentOutOfRangeException(nameof(quantityScale), quantityScale, "Quantity scale must be between 0 and 3.");

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
            UnitCode = catalog?.Code,
            QuantityScale = catalog?.Scale ?? quantityScale ?? 0,
        };

        item.AddDomainEvent(new Events.ItemCreatedDomainEvent(item.Id, item.Barcode, item.Name, item.ShopId));

        return item;
    }

    public void Update(
        string name,
        string? description,
        string barcode,
        bool isActive,
        Guid updatedBy,
        string? hsnCode = null,
        decimal defaultTaxRatePercent = 0m,
        bool defaultTaxIncluded = false)
    {
        Name = name.Trim();
        Description = NormalizeOptional(description);
        Barcode = barcode.Trim();
        IsActive = isActive;
        UpdatedBy = updatedBy;
        UpdateTaxDefaults(hsnCode, defaultTaxRatePercent, defaultTaxIncluded);
    }

    public void MarkUnitReviewRequired() => UnitReviewRequired = true;

    /// <summary>True when applying this label and scale would change the unit identity or the precision (not just the spelling).</summary>
    public bool WouldChangeUnit(string uom, int? quantityScale)
    {
        var (targetCode, targetScale) = ResolveUnit(uom.Trim(), quantityScale);
        return !IsSameIdentity(uom.Trim(), targetCode) || targetScale != QuantityScale;
    }

    private (string? Code, int Scale) ResolveUnit(string label, int? quantityScale)
    {
        var catalog = QuantityUnits.Resolve(label);
        if (catalog is null && quantityScale is < 0 or > QuantityUnits.MaxScale)
            throw new ArgumentOutOfRangeException(nameof(quantityScale), quantityScale, "Quantity scale must be between 0 and 3.");

        return (catalog?.Code, catalog?.Scale ?? quantityScale ?? QuantityScale);
    }

    private bool IsSameIdentity(string label, string? targetCode) =>
        targetCode is not null
            ? string.Equals(targetCode, UnitCode, StringComparison.Ordinal)
            : UnitCode is null && string.Equals(label, Uom, StringComparison.OrdinalIgnoreCase);

    public ErrorOr<Success> ChangeUnit(string uom, int? quantityScale, ItemStockHistory history)
    {
        var label = uom.Trim();
        var (targetCode, targetScale) = ResolveUnit(label, quantityScale);

        if (IsSameIdentity(label, targetCode) && targetScale == QuantityScale)
        {
            Uom = label;
            return Result.Success;
        }

        var allowed = !history.HasMovements
            || (UnitReviewRequired && targetScale >= history.MaxOnHandScale);
        if (!allowed)
            return Errors.ItemUnit.ChangeWithHistory;

        Uom = label;
        UnitCode = targetCode;
        QuantityScale = targetScale;
        UnitReviewRequired = false;
        return Result.Success;
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
