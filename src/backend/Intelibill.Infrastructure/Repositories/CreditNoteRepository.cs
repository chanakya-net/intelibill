using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CreditNoteRepository(ApplicationDbContext context)
    : RepositoryBase<CreditNote>(context), ICreditNoteRepository
{
    private readonly ApplicationDbContext _context = context;
    public async Task<CreditNote?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default) =>
        await DbSet
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Code == code.Trim(), cancellationToken);

    public async Task<IReadOnlyList<CreditNote>> GetByReturnIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(c => c.ShopId == shopId && c.SaleReturnId == saleReturnId)
            .ToListAsync(cancellationToken);

    public async Task<CreditNote?> GetByIdWithRedemptionsAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<CreditNoteListRow> Items, int TotalCount)> GetPagedAsync(
        Guid shopId,
        string? searchTerm,
        CreditNoteStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var query = from cn in _context.CreditNotes.AsNoTracking()
                    join sr in _context.SaleReturns.AsNoTracking() on cn.SaleReturnId equals sr.Id
                    join s in _context.Sales.AsNoTracking() on sr.SaleId equals s.Id
                    where cn.ShopId == shopId
                    select new { cn, sr, s };

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Trim().ToLowerInvariant()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.cn.Code.ToLowerInvariant(), pattern) ||
                EF.Functions.Like(x.sr.ReturnNumber.ToLowerInvariant(), pattern) ||
                EF.Functions.Like(x.s.InvoiceNumber.ToLowerInvariant(), pattern) ||
                (x.s.CustomerName != null && EF.Functions.Like(x.s.CustomerName.ToLowerInvariant(), pattern)));
        }

        query = status switch
        {
            CreditNoteStatus.Active => query.Where(x =>
                !x.cn.IsVoided && x.cn.AvailableBalance > 0 &&
                (!x.cn.ExpiresAt.HasValue || x.cn.ExpiresAt >= now)),
            CreditNoteStatus.Voided => query.Where(x => x.cn.IsVoided),
            CreditNoteStatus.FullyRedeemed => query.Where(x =>
                !x.cn.IsVoided && x.cn.AvailableBalance == 0m),
            CreditNoteStatus.Expired => query.Where(x =>
                !x.cn.IsVoided && x.cn.AvailableBalance > 0 &&
                x.cn.ExpiresAt.HasValue && x.cn.ExpiresAt < now),
            _ => query,
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.cn.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CreditNoteListRow(
                x.cn.Id,
                x.cn.Code,
                x.cn.OriginalAmount,
                x.cn.AvailableBalance,
                x.cn.ExpiresAt,
                x.cn.IsVoided,
                x.cn.CreatedAt,
                x.sr.Id,
                x.sr.ReturnNumber,
                x.s.Id,
                x.s.InvoiceNumber,
                x.s.CustomerName))
            .ToListAsync(cancellationToken);

        return (rows, totalCount);
    }
}
