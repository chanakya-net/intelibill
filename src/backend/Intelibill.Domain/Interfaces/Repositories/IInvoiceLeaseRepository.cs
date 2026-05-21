using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInvoiceLeaseRepository : IRepository<InvoiceLease>
{
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
