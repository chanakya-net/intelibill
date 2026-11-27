using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class HsnCacheRepository(ApplicationDbContext context)
    : RepositoryBase<HsnCache>(context), IHsnCacheRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<HsnCache?> GetByProductNameAsync(string productName, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(h => EF.Functions.ILike(h.ProductName, productName), cancellationToken);
    }

    public async Task SaveAsync(HsnCache entry, CancellationToken cancellationToken = default)
    {
        await _context.HsnCaches.AddAsync(entry, cancellationToken);
    }
}
