using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;
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

    /// <summary>Who reserved the range and from which sign-in; null for leases reserved before this was recorded.</summary>
    public Guid? ReservedByUserId { get; private set; }

    public Guid? ReservedBySessionId { get; private set; }

    /// <summary>
    /// The seller identity in force when the range was reserved; the documents of the range's
    /// sales are issued under it. Null for ranges reserved before documents were recorded.
    /// </summary>
    public InvoiceSellerProfile? SellerProfile { get; private set; }

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
        DateTimeOffset expiresAt,
        Guid? reservedByUserId = null,
        Guid? reservedBySessionId = null,
        InvoiceSellerProfile? sellerProfile = null)
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
            ReservedByUserId = reservedByUserId,
            ReservedBySessionId = reservedBySessionId,
            SellerProfile = sellerProfile,
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
