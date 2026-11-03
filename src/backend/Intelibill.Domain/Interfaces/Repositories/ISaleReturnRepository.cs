using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISaleReturnRepository : IRepository<SaleReturn>
{
    Task<SaleReturn?> GetByIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default);
    /// <summary>The sale a return belongs to, read without tracking: it never changes, so it may be read before the sale lock.</summary>
    Task<Guid?> GetSaleIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default);
    Task<SaleReturn?> GetByIdWithItemsAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default);
    Task<SaleReturn?> GetByReturnNumberAsync(Guid shopId, string returnNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaleReturn>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaleReturn>> GetBySaleAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaleReturnItem>> GetLinesBySaleItemAsync(Guid shopId, Guid saleItemId, CancellationToken cancellationToken = default);
}
