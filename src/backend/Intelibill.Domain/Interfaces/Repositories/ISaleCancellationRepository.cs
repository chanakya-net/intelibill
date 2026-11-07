using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISaleCancellationRepository : IRepository<SaleCancellation>
{
    Task<SaleCancellation?> GetBySaleAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleCancellation>> GetByDateRangeAsync(Guid shopId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
