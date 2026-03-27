using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ShopRepository(ApplicationDbContext context)
    : RepositoryBase<Shop>(context), IShopRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<IReadOnlyList<ShopMembership>> GetMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _context.ShopMemberships
            .Where(sm => sm.UserId == userId)
            .Include(sm => sm.Shop)
            .OrderByDescending(sm => sm.LastUsedAt)
            .ThenByDescending(sm => sm.IsDefault)
            .ToListAsync(cancellationToken);

    public async Task<ShopMembership?> GetMembershipAsync(
        Guid userId,
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        await _context.ShopMemberships
            .Include(sm => sm.Shop)
            .FirstOrDefaultAsync(sm => sm.UserId == userId && sm.ShopId == shopId, cancellationToken);

    public async Task<ShopMembership?> GetDefaultMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _context.ShopMemberships
            .Include(sm => sm.Shop)
            .FirstOrDefaultAsync(sm => sm.UserId == userId && sm.IsDefault, cancellationToken);

    public async Task<ShopMembership?> GetMostRecentlyUsedMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _context.ShopMemberships
            .Include(sm => sm.Shop)
            .Where(sm => sm.UserId == userId)
            .OrderByDescending(sm => sm.LastUsedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> UserHasMembershipAsync(Guid userId, Guid shopId, CancellationToken cancellationToken = default) =>
        await _context.ShopMemberships.AnyAsync(sm => sm.UserId == userId && sm.ShopId == shopId, cancellationToken);

    public async Task ClearDefaultForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await _context.ShopMemberships
            .Where(sm => sm.UserId == userId && sm.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var membership in memberships)
        {
            membership.SetDefault(false);
        }
    }

    public async Task<Shop?> GetByIdWithMembersAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Memberships)
            .ThenInclude(sm => sm.User)
            .FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
}