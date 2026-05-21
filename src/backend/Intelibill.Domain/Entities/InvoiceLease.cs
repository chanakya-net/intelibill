using Intelibill.Domain.Common;
using System.Globalization;

namespace Intelibill.Domain.Entities;

public sealed class InvoiceLease : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid InvoiceSequenceId { get; private set; }
    public int FiscalYearStart { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public int RangeStart { get; private set; }
    public int RangeEnd { get; private set; }
    public int NextNumber { get; private set; }
    public int NumberPadding { get; private set; }
    public DateTimeOffset ReservedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public InvoiceSequence InvoiceSequence { get; private set; } = null!;

    private InvoiceLease() { }

    public static InvoiceLease Create(
        Guid shopId,
        Guid invoiceSequenceId,
        string deviceId,
        int fiscalYearStart,
        string prefix,
        int rangeStart,
        int rangeEnd,
        int numberPadding,
        DateTimeOffset reservedAt,
        DateTimeOffset expiresAt)
    {
        return new InvoiceLease
        {
            ShopId = shopId,
            InvoiceSequenceId = invoiceSequenceId,
            DeviceId = deviceId.Trim(),
            FiscalYearStart = fiscalYearStart,
            Prefix = prefix.Trim(),
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            NextNumber = rangeStart,
            NumberPadding = numberPadding,
            ReservedAt = reservedAt,
            ExpiresAt = expiresAt,
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public int RemainingCount => RangeEnd >= NextNumber ? RangeEnd - NextNumber + 1 : 0;

    public string FormatInvoiceNumber(int number) =>
        $"{Prefix}{number.ToString($"D{NumberPadding}", CultureInfo.InvariantCulture)}";

    public void UpdateNextNumber(int nextNumber)
    {
        NextNumber = nextNumber;
    }
}
