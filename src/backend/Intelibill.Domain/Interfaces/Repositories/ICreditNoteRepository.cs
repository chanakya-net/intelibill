using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICreditNoteRepository : IRepository<CreditNote>
{
    // Callers should pass normalized credit note codes to match repository lookup behavior.
    Task<CreditNote?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default);
    // Callers should pass normalized credit note codes to match repository lookup behavior.
    Task<CreditNote?> GetByCodeWithRedemptionsAsync(Guid shopId, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNote>> GetByReturnIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNote>> GetByReturnIdsAsync(Guid shopId, IReadOnlyCollection<Guid> saleReturnIds, CancellationToken cancellationToken = default);
    Task<CreditNote?> GetByIdWithRedemptionsAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default);
    Task<CreditNote?> GetBySaleReturnIdWithRedemptionsAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default);
    Task AddRedemptionAsync(CreditNoteRedemption redemption, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNoteRedemptionListRow>> GetRedemptionsBySaleIdAsync(
        Guid shopId,
        Guid saleId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CreditNoteListRow> Items, int TotalCount)> GetPagedAsync(
        Guid shopId,
        string? searchTerm,
        CreditNoteStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed record CreditNoteListRow(
    Guid CreditNoteId,
    string Code,
    decimal OriginalAmount,
    decimal AvailableBalance,
    DateTimeOffset? ExpiresAt,
    bool IsVoided,
    DateTimeOffset CreatedAt,
    Guid SaleReturnId,
    string ReturnNumber,
    Guid SaleId,
    string InvoiceNumber,
    string? CustomerName);

public sealed record CreditNoteRedemptionListRow(
    Guid CreditNoteId,
    string Code,
    decimal Amount);
