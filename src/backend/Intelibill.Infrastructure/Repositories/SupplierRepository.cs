using Intelibill.Domain.Entities;
using Intelibill.Domain.ReadModels;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SupplierRepository(ApplicationDbContext context)
    : RepositoryBase<Supplier>(context), ISupplierRepository
{

    public async Task<PartyDirectoryPage<Supplier>> GetDirectoryAsync(Guid shopId, PartyDirectoryQuery query, bool includeSystem, CancellationToken cancellationToken = default)
    {
        var parties = DbSet.AsNoTracking().Where(p => p.ShopId == shopId && (includeSystem || !p.IsSystem));
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = PartyDirectorySearch.Pattern(search);
            parties = parties.Where(p => EF.Functions.ILike((p.IsSystem ? "Unassigned" : p.Name), pattern, "\\")
                || EF.Functions.ILike((p.ContactPersonName ?? ""), pattern, "\\")
                || EF.Functions.ILike((p.ContactPersonPhone ?? ""), pattern, "\\")
                || EF.Functions.ILike((p.City ?? ""), pattern, "\\"));
        }
        var status = query.Status.Trim().ToLowerInvariant();
        if (status != "all") parties = parties.Where(p => p.IsActive == (status == "active"));
        var count = await parties.CountAsync(cancellationToken);
        var items = await parties.OrderBy(p => p.IsSystem ? "Unassigned" : p.Name).ThenBy(p => p.Id)
            .Skip(checked((query.PageNumber - 1) * query.PageSize)).Take(query.PageSize).ToListAsync(cancellationToken);
        return new(items, count, query.PageNumber, query.PageSize);
    }

    public async Task<SupplierDirectorySummary> GetDirectorySummaryAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default)
    {
        var parties = DbSet.AsNoTracking().Where(p => p.ShopId == shopId && (includeSystem || !p.IsSystem));
        var summary = await parties.GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Active = g.Count(p => p.IsActive), Preferred = g.Count(p => p.IsPreferred) })
            .SingleOrDefaultAsync(cancellationToken);
        var payables = await PartyLedgerQueries.Suppliers(Context.Set<SupplierLedgerEntry>()
                .Where(e => e.ShopId == shopId && parties.Any(p => p.Id == e.SupplierId)))
            .Where(b => b.Amount > 0m).SumAsync(b => b.Amount, cancellationToken);
        return new(summary?.Total ?? 0, summary?.Active ?? 0, summary?.Preferred ?? 0, payables);
    }

    public async Task<IReadOnlyList<Supplier>> GetByShopIdAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.ShopId == shopId && (includeSystem || !s.IsSystem))
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<Supplier?> GetSystemByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.ShopId == shopId && s.IsSystem)
            .FirstOrDefaultAsync(cancellationToken);
}
