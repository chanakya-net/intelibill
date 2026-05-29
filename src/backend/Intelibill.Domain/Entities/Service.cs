using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Service : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public string? HsnCode { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public bool TaxIncluded { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    private Service() { }

    public static Service Create(
        Guid shopId,
        string code,
        string name,
        string? description,
        decimal price,
        string? hsnCode,
        decimal taxRatePercent,
        bool taxIncluded,
        bool isActive,
        Guid createdBy)
    {
        return new Service
        {
            ShopId = shopId,
            Code = code.Trim(),
            Name = name.Trim(),
            Description = NormalizeOptional(description),
            Price = price,
            HsnCode = NormalizeOptional(hsnCode),
            TaxRatePercent = taxRatePercent,
            TaxIncluded = taxIncluded,
            IsActive = isActive,
            CreatedBy = createdBy,
        };
    }

    public void Update(
        string name,
        string? description,
        decimal price,
        string? hsnCode,
        decimal taxRatePercent,
        bool taxIncluded,
        Guid updatedBy)
    {
        Name = name.Trim();
        Description = NormalizeOptional(description);
        Price = price;
        HsnCode = NormalizeOptional(hsnCode);
        TaxRatePercent = taxRatePercent;
        TaxIncluded = taxIncluded;
        UpdatedBy = updatedBy;
    }

    public void Activate(Guid updatedBy)
    {
        if (IsActive)
            return;

        IsActive = true;
        UpdatedBy = updatedBy;
    }

    public void Deactivate(Guid updatedBy)
    {
        if (!IsActive)
            return;

        IsActive = false;
        UpdatedBy = updatedBy;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
