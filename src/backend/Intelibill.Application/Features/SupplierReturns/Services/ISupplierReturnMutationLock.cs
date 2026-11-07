using Intelibill.Domain.Interfaces.Repositories;
namespace Intelibill.Application.Features.SupplierReturns.Services;
public interface ISupplierReturnMutationLock
{
    Task<RowLockOutcome> LockAsync(Guid shopId, Guid supplierId, IReadOnlyCollection<Guid> receiptValueIds, CancellationToken cancellationToken);
    Task<RowLockOutcome> LockReturnAsync(Guid shopId, Guid returnId, CancellationToken cancellationToken);
}
