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
    public Task<CreditNote?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default) =>
        FindByCodeAsync(shopId, code, includeRedemptions: false, cancellationToken);

    public Task<CreditNote?> GetByCodeWithRedemptionsAsync(Guid shopId, string code, CancellationToken cancellationToken = default) =>
        FindByCodeAsync(shopId, code, includeRedemptions: true, cancellationToken);

    public async Task<CreditNote?> GetByCodeForUpdateWithRedemptionsAsync(Guid shopId, string code, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsNpgsql())
        {
            return await GetByCodeWithRedemptionsAsync(shopId, code, cancellationToken);
        }

        var normalizedCode = NormalizeLookupCode(code);

        return await DbSet
            .FromSqlRaw(
                """
                SELECT *
                FROM credit_notes
                WHERE shop_id = {0}
                    AND REPLACE(REPLACE(UPPER(code), '-', ''), ' ', '') = {1}
                FOR UPDATE
                """,
                shopId,
                normalizedCode)
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<CreditNote?> FindByCodeAsync(
        Guid shopId,
        string code,
        bool includeRedemptions,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeLookupCode(code);

        IQueryable<CreditNote> query = DbSet;
        if (includeRedemptions)
        {
            query = query.Include(c => c.Redemptions);
        }

        return query.FirstOrDefaultAsync(
            c => c.ShopId == shopId &&
                 // The existing unique index is on the stored code value, so normalized lookups
                 // intentionally apply string functions here instead of using the raw index.
                 // Credit note volume is expected to stay low enough that this per-shop scan is acceptable.
                 c.Code.Replace("-", string.Empty).Replace(" ", string.Empty) == normalizedCode,
            cancellationToken);
    }

    private static string NormalizeLookupCode(string code) =>
        code.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    public async Task<IReadOnlyList<CreditNote>> GetByReturnIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(c => c.ShopId == shopId && c.SaleReturnId == saleReturnId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CreditNote>> GetByReturnIdsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> saleReturnIds,
        CancellationToken cancellationToken = default)
    {
        if (saleReturnIds.Count == 0)
        {
            return [];
        }

        return await DbSet
            .Where(c => c.ShopId == shopId && saleReturnIds.Contains(c.SaleReturnId))
            .ToListAsync(cancellationToken);
    }

    public async Task<CreditNote?> GetByIdWithRedemptionsAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == id, cancellationToken);

    public async Task<CreditNote?> GetBySaleReturnIdWithRedemptionsAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.SaleReturnId == saleReturnId, cancellationToken);

    public async Task AddRedemptionAsync(CreditNoteRedemption redemption, CancellationToken cancellationToken = default) =>
        await _context.CreditNoteRedemptions.AddAsync(redemption, cancellationToken);

    public async Task<IReadOnlyList<CreditNoteRedemptionListRow>> GetRedemptionsBySaleIdAsync(
        Guid shopId,
        Guid saleId,
        CancellationToken cancellationToken = default) =>
        await (from redemption in _context.CreditNoteRedemptions.AsNoTracking()
               join note in _context.CreditNotes.AsNoTracking() on redemption.CreditNoteId equals note.Id
               where redemption.ShopId == shopId && redemption.SaleId == saleId
               orderby redemption.CreatedAt, redemption.Id
               select new CreditNoteRedemptionListRow(
                   redemption.CreditNoteId,
                   note.Code,
                   redemption.Amount)).ToListAsync(cancellationToken);

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
