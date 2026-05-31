using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ItemBarcodeSequenceRepository(ApplicationDbContext context) : IItemBarcodeSequenceRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<ItemBarcodeSequence?> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await _context.ItemBarcodeSequences.FirstOrDefaultAsync(
            sequence => sequence.ShopId == shopId,
            cancellationToken);

    public async Task AddAsync(ItemBarcodeSequence sequence, CancellationToken cancellationToken = default) =>
        await _context.ItemBarcodeSequences.AddAsync(sequence, cancellationToken);
}
