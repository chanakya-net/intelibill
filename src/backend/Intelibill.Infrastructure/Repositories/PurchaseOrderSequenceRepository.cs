using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseOrderSequenceRepository : RepositoryBase<PurchaseOrderSequence>, IPurchaseOrderSequenceRepository
{
    private readonly ApplicationDbContext _context;

    public PurchaseOrderSequenceRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }
    public async Task<PurchaseOrderSequence> GetOrCreateByShopAndYearAsync(
        Guid shopId,
        int year,
        CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO purchase_order_sequences (id, shop_id, year, next_number, created_at, updated_at)
            VALUES ({0}, {1}, {2}, {3}, {4}, NULL)
            ON CONFLICT (shop_id, year) DO NOTHING;
            """,
            Guid.NewGuid(),
            shopId,
            year,
            1,
            DateTimeOffset.UtcNow);

        var sequence = await _context.PurchaseOrderSequences
            .FromSqlRaw(
                """
                SELECT * FROM purchase_order_sequences
                WHERE shop_id = {0} AND year = {1}
                FOR UPDATE
                """,
                shopId,
                year)
            .SingleAsync(cancellationToken);

        return sequence;
    }

    public async Task<PurchaseOrderSequence?> GetByShopAndYearAsync(
        Guid shopId,
        int year,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .FirstOrDefaultAsync(s => s.ShopId == shopId && s.Year == year, cancellationToken);
}
