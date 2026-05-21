using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class InvoiceSequence : BaseEntity
{
    public Guid ShopId { get; private set; }
    public int FiscalYearStart { get; private set; }
    public int NextNumber { get; private set; }
    public string Prefix { get; private set; } = string.Empty;

    private InvoiceSequence() { }

    public static InvoiceSequence Create(Guid shopId, FiscalYear fiscalYear)
    {
        return new InvoiceSequence
        {
            ShopId = shopId,
            FiscalYearStart = fiscalYear.StartYear,
            NextNumber = 1,
            Prefix = fiscalYear.InvoicePrefix,
        };
    }

    public (int Start, int End) ReserveBlock(int blockSize)
    {
        if (blockSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize), "Block size must be positive.");
        }

        var start = NextNumber;
        var end = checked(start + blockSize - 1);
        NextNumber = end + 1;
        return (start, end);
    }
}
