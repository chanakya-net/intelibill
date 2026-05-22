using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class InvoiceLeaseTests
{
    [Fact]
    public void FormatInvoiceNumber_UsesPrefixAndPadding()
    {
        var lease = InvoiceLease.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            deviceId: "device-1",
            fiscalYearStart: 2025,
            prefix: "INV-2025-26-",
            rangeStart: 1,
            rangeEnd: 200,
            numberPadding: 6,
            reservedAt: new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            expiresAt: new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeSpan.Zero));

        var invoiceNumber = lease.FormatInvoiceNumber(42);

        Assert.Equal("INV-2025-26-000042", invoiceNumber);
    }

    [Fact]
    public void RemainingCount_UsesNextNumberWithinRange()
    {
        var lease = InvoiceLease.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            deviceId: "device-2",
            fiscalYearStart: 2025,
            prefix: "INV-2025-26-",
            rangeStart: 10,
            rangeEnd: 15,
            numberPadding: 6,
            reservedAt: DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddDays(7));

        lease.UpdateNextNumber(12);

        Assert.Equal(4, lease.RemainingCount);
    }

    [Fact]
    public void IsExpired_WhenBeyondExpiry_ReturnsTrue()
    {
        var lease = InvoiceLease.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            deviceId: "device-3",
            fiscalYearStart: 2025,
            prefix: "INV-2025-26-",
            rangeStart: 1,
            rangeEnd: 200,
            numberPadding: 6,
            reservedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            expiresAt: new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero));

        Assert.True(lease.IsExpired(new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero)));
    }
}
