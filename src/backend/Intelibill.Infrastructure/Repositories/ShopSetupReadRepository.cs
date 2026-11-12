using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ShopSetupReadRepository(ApplicationDbContext db) : IShopSetupReadRepository
{
    public Task<ShopSetupMember?> GetMemberAsync(Guid shopId, Guid userId, CancellationToken ct) =>
        db.ShopMemberships.AsNoTracking()
            .Where(m => m.ShopId == shopId && m.UserId == userId && m.Status == MembershipStatus.Active && m.User.IsLoginEnabled)
            .Select(m => new ShopSetupMember(m.Shop, m.Role))
            .SingleOrDefaultAsync(ct);

    public async Task<ShopSetupFacts> GetFactsAsync(Guid shopId, CancellationToken ct) =>
        new(
            await db.ShopMemberships.AnyAsync(m => m.ShopId == shopId && m.Status == MembershipStatus.Active && m.Role == ShopRole.Owner
                && m.User.IsLoginEnabled && (m.User.IsEmailVerified || m.User.IsPhoneVerified), ct),
            await db.Items.AnyAsync(item => item.ShopId == shopId && item.IsActive, ct),
            await db.Services.AnyAsync(service => service.ShopId == shopId && service.IsActive, ct),
            await db.BankAccounts.AnyAsync(account => account.ShopId == shopId && account.BankName != "" && account.AccountNumber != "", ct));

    public async Task<IReadOnlyList<ShopSetupGoodsCandidate>> GetGoodsAsync(Guid shopId, int offset, int limit, CancellationToken ct) =>
        await db.InventoryBatches.AsNoTracking()
            .Where(batch => batch.ShopId == shopId && batch.Item.ShopId == shopId && batch.Item.IsActive && !batch.IsVoided && batch.Quantity > 0)
            .OrderBy(batch => batch.Item.Name).ThenBy(batch => batch.ItemId)
            .ThenBy(batch => batch.ExpiryDate == null).ThenBy(batch => batch.ExpiryDate)
            .ThenBy(batch => EF.Functions.Collate(batch.BatchNumber, "C")).ThenBy(batch => batch.Id)
            .Skip(offset).Take(limit)
            .Select(batch => new ShopSetupGoodsCandidate(batch.Item, batch,
                db.Inventory.Where(inventory => inventory.ShopId == shopId && inventory.ItemId == batch.ItemId)
                    .Select(inventory => (decimal?)inventory.Quantity).FirstOrDefault()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Service>> GetServicesAsync(Guid shopId, int offset, int limit, CancellationToken ct) =>
        await db.Services.AsNoTracking()
            .Where(service => service.ShopId == shopId && service.IsActive)
            .OrderBy(service => service.Name).ThenBy(service => service.Id)
            .Skip(offset).Take(limit)
            .ToListAsync(ct);
}
