using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInvoiceLeaseRepository : IRepository<InvoiceLease>
{
    IAsyncEnumerable<InvoiceLease> StreamActiveByShopAsync(Guid shopId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceLease>> GetActiveByDeviceAsync(
        Guid shopId,
        string deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<InvoiceLease> ReserveAsync(
        Guid shopId,
        string deviceId,
        int fiscalYearStart,
        string prefix,
        int blockSize,
        DateTimeOffset reservedAt,
        DateTimeOffset expiresAt,
        Guid? reservedByUserId,
        Guid? reservedBySessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The lease whose prefix and numeric range contain the invoice number on the device, expiry ignored.
    /// </summary>
    Task<InvoiceLease?> FindReservedRangeAsync(
        Guid shopId,
        string deviceId,
        string invoiceNumber,
        CancellationToken cancellationToken = default);
}
