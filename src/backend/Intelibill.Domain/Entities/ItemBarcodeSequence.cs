using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class ItemBarcodeSequence : BaseEntity
{
    public const string CodePrefix = "IB";

    public Guid ShopId { get; private set; }
    public int NextNumber { get; private set; }
    public string Prefix { get; private set; } = string.Empty;

    private ItemBarcodeSequence() { }

    public static ItemBarcodeSequence Create(Guid shopId)
    {
        return new ItemBarcodeSequence
        {
            ShopId = shopId,
            NextNumber = 1,
            Prefix = CodePrefix,
        };
    }

    public string NextCode()
    {
        var code = $"{Prefix}-{NextNumber:D6}";
        NextNumber++;
        return code;
    }
}
