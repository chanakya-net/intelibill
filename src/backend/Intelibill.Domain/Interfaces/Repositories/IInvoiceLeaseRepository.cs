using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInvoiceLeaseRepository : IRepository<InvoiceLease>
{
    IAsyncEnumerable<InvoiceLease> StreamActiveByShopAsync(Guid shopId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<InvoiceLease> ReserveAsync(
        Guid shopId,
        string deviceId,
        int fiscalYearStart,
        string prefix,
        int blockSize,
        DateTimeOffset reservedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
