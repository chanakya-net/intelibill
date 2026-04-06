using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISupplierLedgerEntryRepository : IRepository<SupplierLedgerEntry>
{
    Task<IReadOnlyList<SupplierLedgerEntry>> GetBySupplierAsync(Guid shopId, Guid supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierLedgerEntry>> GetByBatchAsync(Guid shopId, Guid batchId, CancellationToken cancellationToken = default);
}
