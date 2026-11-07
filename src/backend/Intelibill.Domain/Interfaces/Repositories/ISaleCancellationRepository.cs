using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISaleCancellationRepository : IRepository<SaleCancellation>
{
    Task<SaleCancellation?> GetBySaleAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleCancellation>> GetBySaleIdsAsync(Guid shopId, IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleCancellation>> GetPostedInRangeAsync(Guid shopId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
