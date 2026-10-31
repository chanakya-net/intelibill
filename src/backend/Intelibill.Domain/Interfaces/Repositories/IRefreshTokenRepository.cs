using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    /// <summary>
    /// Names the account and session a presented credential belongs to, so their locks can be
    /// taken in order. Untracked, and decides nothing: by the time the locks are held it may be
    /// stale, which is why <see cref="LockAsync"/> reads the credential again.
    /// </summary>
    Task<RefreshTokenOwner?> FindOwnerByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Locks the credential's row and returns it with the values read under that lock. Lock order
    /// is users -> auth_sessions -> refresh_tokens, so take the account and session locks first.
    /// </summary>
    Task<RefreshToken?> LockAsync(Guid tokenId, CancellationToken ct);
}
