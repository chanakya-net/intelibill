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

    public async Task<CreditNote?> GetByCodeWithRedemptionsAsync(Guid shopId, string code, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Code == code.Trim(), cancellationToken);

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
            .AsNoTracking()
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == id, cancellationToken);

    public async Task<CreditNote?> GetForRedemptionForUpdateAsync(Guid shopId, decimal requestedAmount, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsNpgsql())
        {
            return await GetForRedemptionAsync(shopId, requestedAmount, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        return await DbSet
            .FromSqlRaw(
                """
                SELECT * FROM credit_notes
                WHERE shop_id = {0}
                    AND NOT is_voided
                    AND (expires_at IS NULL OR expires_at >= {1})
                    AND available_balance >= {2}
                ORDER BY created_at, COALESCE(expires_at, 'infinity'::timestamp with time zone)
                FOR UPDATE
                """,
                shopId,
                now,
                requestedAmount)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryApplyRedemptionAsync(
        Guid shopId,
        Guid creditNoteId,
        Guid saleId,
        decimal amount,
        DateTimeOffset redeemedAt,
        CancellationToken cancellationToken = default)
    {
        var note = await DbSet
            .Where(c =>
                c.ShopId == shopId &&
                c.Id == creditNoteId &&
                !c.IsVoided &&
                (c.ExpiresAt == null || c.ExpiresAt >= redeemedAt) &&
                c.AvailableBalance >= amount)
            .FirstOrDefaultAsync(cancellationToken);

        if (note is null)
            return false;

        var redemptionResult = note.Redeem(shopId, saleId, amount);
        if (redemptionResult.IsError)
        {
            return false;
        }

        _context.CreditNoteRedemptions.Add(redemptionResult.Value);

        var result = await _context.SaveChangesAsync(cancellationToken);
        return result > 0;
    }

    public async Task<CreditNote?> GetForRedemptionAsync(Guid shopId, decimal requestedAmount, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await DbSet
            .Where(c => c.ShopId == shopId
                && !c.IsVoided
                && (c.ExpiresAt == null || c.ExpiresAt >= now)
                && c.AvailableBalance >= requestedAmount)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.ExpiresAt ?? DateTimeOffset.MaxValue)
            .FirstOrDefaultAsync(cancellationToken);
    }

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
