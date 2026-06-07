using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseOrderReceiptSequenceRepository : RepositoryBase<PurchaseOrderReceiptSequence>, IPurchaseOrderReceiptSequenceRepository
{
    private readonly ApplicationDbContext _context;

    public PurchaseOrderReceiptSequenceRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<PurchaseOrderReceiptSequence> GetOrCreateByShopAndYearAsync(
        Guid shopId,
        int year,
        CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO purchase_order_receipt_sequences (id, shop_id, year, next_number, created_at, updated_at)
            VALUES ({0}, {1}, {2}, {3}, {4}, NULL)
            ON CONFLICT (shop_id, year) DO NOTHING;
            """,
            Guid.NewGuid(),
            shopId,
            year,
            1,
            DateTimeOffset.UtcNow);

        return await _context.PurchaseOrderReceiptSequences
            .FromSqlRaw(
                """
                SELECT * FROM purchase_order_receipt_sequences
                WHERE shop_id = {0} AND year = {1}
                FOR UPDATE
                """,
                shopId,
                year)
            .SingleAsync(cancellationToken);
    }
}
