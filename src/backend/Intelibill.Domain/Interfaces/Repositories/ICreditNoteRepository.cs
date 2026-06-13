using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICreditNoteRepository : IRepository<CreditNote>
{
    Task<CreditNote?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default);
    Task<CreditNote?> GetByCodeWithRedemptionsAsync(Guid shopId, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNote>> GetByReturnIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default);
    Task<CreditNote?> GetByIdWithRedemptionsAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default);
}
