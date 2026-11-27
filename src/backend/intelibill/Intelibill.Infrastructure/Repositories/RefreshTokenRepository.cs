using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class RefreshTokenRepository(ApplicationDbContext context)
    : RepositoryBase<RefreshToken>(context), IRefreshTokenRepository
{
    public Task<RefreshTokenOwner?> FindOwnerByHashAsync(string tokenHash, CancellationToken ct) =>
        DbSet
            .AsNoTracking()
            .Where(x => x.TokenHash == tokenHash)
            .Select(x => new RefreshTokenOwner(x.Id, x.UserId, x.AuthSessionId))
            .FirstOrDefaultAsync(ct);

    public Task<RefreshToken?> LockAsync(Guid tokenId, CancellationToken ct) =>
        Context.LockTrackedAsync(
            DbSet.FromSql($"SELECT * FROM refresh_tokens WHERE id = {tokenId} FOR UPDATE"),
            tokenId,
            ct);
}
