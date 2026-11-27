using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class AuthSessionRepository(ApplicationDbContext db, IUnitOfWork unitOfWork)
    : IAuthSessionRepository
{
    public async Task<IAsyncDisposable> BeginAsync(CancellationToken ct) =>
        await db.Database.BeginTransactionAsync(ct);

    public async Task<UserAuthority?> LockUserAuthorityAsync(Guid userId, CancellationToken ct)
    {
        _ = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE id = {userId} FOR UPDATE")
            .ToListAsync(ct);

        // Projected and untracked so the values come from the database rather than from a user
        // instance loaded before the lock was taken. Identity resolution would otherwise hand back
        // the pre-lock security version, which is the whole thing the lock exists to prevent.
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserAuthority(u.Id, u.SecurityVersion, u.IsLoginEnabled, u.Email != null, u.IsEmailVerified))
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(AuthSession session, CancellationToken ct) =>
        await db.AuthSessions.AddAsync(session, ct);

    public Task<AuthSession?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        db.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);

    public Task<AuthSession?> LockAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        db.LockTrackedAsync(
            db.AuthSessions.FromSql($"SELECT * FROM auth_sessions WHERE id = {sessionId} AND user_id = {userId} FOR UPDATE"),
            sessionId,
            ct);

    public async Task<IReadOnlyList<AuthSession>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.AuthSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(ct);

    public async Task<int> RevokeAllAsync(
        Guid userId,
        Guid? exceptSessionId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Set-based: a compromised account can hold many devices, and loading them all to call
        // Revoke on each would turn one security transition into an unbounded round trip.
        var revoked = await db.AuthSessions
            .Where(x => x.UserId == userId
                && x.RevokedAt == null
                && (exceptSessionId == null || x.Id != exceptSessionId.Value))
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevocationReason, reason),
                ct);

        _ = await db.RefreshTokens
            .Where(x => x.UserId == userId && (exceptSessionId == null || x.AuthSessionId != exceptSessionId.Value))
            .RevokeAsync(reason, now, ct);

        return revoked;
    }

    public async Task RevokeAsync(AuthSession session, string reason, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.Revoke(reason, now);

        // The session is the rotation family: every credential it ever issued ends with it,
        // including one minted by a renewal that committed a moment ago.
        _ = await db.RefreshTokens
            .Where(x => x.AuthSessionId == session.Id)
            .RevokeAsync(reason, now, ct);
    }

    public async Task<bool> CommitAsync(CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            return false;
        }
    }
}
