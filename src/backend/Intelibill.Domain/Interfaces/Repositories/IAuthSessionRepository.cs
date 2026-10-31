using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

/// <summary>
/// Device sessions and the account lock that orders every security transition over them. The lock
/// order is users -> auth_sessions -> refresh_tokens, the same order password recovery and
/// membership administration take, so these transitions cannot deadlock against each other.
/// </summary>
public interface IAuthSessionRepository
{
    Task<IAsyncDisposable> BeginAsync(CancellationToken ct);

    /// <summary>
    /// Takes the account row lock and reads the authority that follows from it. The read goes to
    /// the database rather than to anything already tracked, so a value loaded before the lock
    /// cannot be mistaken for the current one.
    /// </summary>
    Task<UserAuthority?> LockUserAuthorityAsync(Guid userId, CancellationToken ct);

    Task AddAsync(AuthSession session, CancellationToken ct);

    Task<AuthSession?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct);

    Task<IReadOnlyList<AuthSession>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Revokes the account's sessions and their refresh families, optionally sparing one session.
    /// Returns how many sessions were ended.
    /// </summary>
    Task<int> RevokeAllAsync(Guid userId, Guid? exceptSessionId, string reason, DateTimeOffset now, CancellationToken ct);

    Task RevokeAsync(AuthSession session, string reason, DateTimeOffset now, CancellationToken ct);

    Task<bool> CommitAsync(CancellationToken ct);
}
