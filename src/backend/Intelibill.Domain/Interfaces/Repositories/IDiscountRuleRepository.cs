using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IDiscountRuleRepository : IRepository<DiscountRule>
{
    Task<IReadOnlyList<DiscountRule>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscountRule>> GetActiveByShopAsync(Guid shopId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscountRule>> GetUpcomingByShopAsync(Guid shopId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscountRule>> GetActiveByBatchAsync(Guid shopId, Guid batchId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscountRule>> GetDisabledByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
}
