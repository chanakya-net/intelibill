using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class InvoiceSequence : BaseEntity
{
    public Guid ShopId { get; private set; }
    public int FiscalYearStart { get; private set; }
    public int NextNumber { get; private set; }
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>The series code, e.g. <c>A</c>.</summary>
    public string SeriesCode { get; private set; } = string.Empty;

    /// <summary>The registration the series is unique for: the GSTIN, or <c>shop:{id}</c> when unregistered.</summary>
    public string SellerRegistrationKey { get; private set; } = string.Empty;

    private InvoiceSequence() { }

    /// <summary>
    /// A fresh counter for one series in one fiscal year. Postings open it with the same values through
    /// <c>InvoiceNumberAllocator</c>, which inserts it under the series lock.
    /// </summary>
    public static InvoiceSequence Open(Guid shopId, string sellerRegistrationKey, string seriesCode, FiscalYear fiscalYear)
    {
        return new InvoiceSequence
        {
            ShopId = shopId,
            FiscalYearStart = fiscalYear.StartYear,
            NextNumber = 1,
            Prefix = InvoiceNumberFormat.Prefix(seriesCode, fiscalYear),
            SeriesCode = seriesCode,
            SellerRegistrationKey = sellerRegistrationKey,
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
