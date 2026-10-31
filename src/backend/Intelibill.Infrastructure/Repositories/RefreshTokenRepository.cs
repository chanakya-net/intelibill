using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class RefreshTokenRepository(ApplicationDbContext context)
    : RepositoryBase<RefreshToken>(context), IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetActiveByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(rt => rt.User)
            .ThenInclude(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .FirstOrDefaultAsync(rt => rt.Token == token && !rt.IsRevoked && rt.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await DbSet
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens)
            t.Revoke();
    }
}
