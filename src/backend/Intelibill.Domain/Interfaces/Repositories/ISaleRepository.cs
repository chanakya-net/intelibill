using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISaleRepository : IRepository<Sale>
{
    Task<Sale?> GetByIdAsync(Guid saleId, Guid shopId, CancellationToken cancellationToken = default);
    Task<Sale?> GetByIdempotencyKeyAsync(Guid shopId, Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<Sale?> GetByClientSaleIdAsync(Guid shopId, string deviceId, string clientSaleId, CancellationToken cancellationToken = default);
    Task<Sale?> GetByInvoiceNumberAsync(Guid shopId, string invoiceNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByCustomerAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByShopAndDateAsync(Guid shopId, DateOnly reportingDay, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}
