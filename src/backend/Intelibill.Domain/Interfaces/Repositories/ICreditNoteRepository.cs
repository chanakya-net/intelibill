using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICreditNoteRepository : IRepository<CreditNote>
{
    Task<CreditNote?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNote>> GetBySaleIdAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken = default);
    Task<CreditNote?> GetByIdWithRedemptionsAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default);
}
