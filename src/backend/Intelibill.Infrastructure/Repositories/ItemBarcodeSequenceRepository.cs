using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ItemBarcodeSequenceRepository(ApplicationDbContext context) : IItemBarcodeSequenceRepository
{
    public async Task<string> GetNextCodeAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Barcode allocation requires the source transaction.");

        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO item_barcode_sequences (id, shop_id, next_number, prefix, created_at)
            VALUES ({Guid.NewGuid()}, {shopId}, 1, {ItemBarcodeSequence.CodePrefix}, {DateTimeOffset.UtcNow})
            ON CONFLICT (shop_id) DO NOTHING;
            """, cancellationToken);

        var sequence = await context.ItemBarcodeSequences
            .FromSqlInterpolated($"SELECT * FROM item_barcode_sequences WHERE shop_id = {shopId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var code = sequence.NextCode();
        await context.SaveChangesAsync(cancellationToken);
        return code;
    }
}
