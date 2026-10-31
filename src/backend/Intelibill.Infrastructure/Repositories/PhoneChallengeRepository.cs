using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PhoneChallengeRepository(ApplicationDbContext db, IUnitOfWork unitOfWork) : IPhoneChallengeRepository
{
    public async Task<IAsyncDisposable> BeginAsync(CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        // The same identity lock the email ownership flow takes. One account's phone and email
        // transitions must serialize against each other, not only against their own kind.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7463218941)", ct);
        return transaction;
    }

    public Task<PhoneChallenge?> FindForUpdateAsync(Guid id, CancellationToken ct) =>
        db.LockTrackedAsync(
            db.Set<PhoneChallenge>().FromSqlInterpolated($"SELECT * FROM phone_challenges WHERE id = {id} FOR UPDATE"),
            id,
            ct);

    public async Task SupersedeAsync(string destination, string purpose, Guid? userId, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.Set<PhoneChallenge>()
            .Where(x => x.Purpose == purpose && x.ConsumedAt == null &&
                (x.Destination == destination || (userId != null && x.UserId == userId)))
            .ToListAsync(ct);

        foreach (var challenge in open) challenge.Consume(now);
    }

    public async Task<bool> HasSendBudgetAsync(
        string destination,
        DateTimeOffset now,
        int sendsPerHour,
        TimeSpan resendSpacing,
        CancellationToken ct)
    {
        var recent = db.Set<IdentitySms>().Where(x => x.Destination == destination && x.CreatedAt > now.AddHours(-1));
        return await recent.CountAsync(ct) < sendsPerHour
            && !await recent.AnyAsync(x => x.CreatedAt > now - resendSpacing, ct);
    }

    public async Task<PhoneOccupant?> LockOccupantAsync(string destination, CancellationToken ct)
    {
        // At most one row: phone_number carries a unique index filtered on NOT NULL.
        var holder = await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE phone_number = {destination} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        return holder is null
            ? null
            : new PhoneOccupant(
                holder.Id,
                holder.SecurityVersion,
                holder.IsLoginEnabled,
                holder.IsPhoneVerified,
                holder.Email is not null,
                holder.IsEmailVerified,
                holder.RequiresOperatorRecovery);
    }

    public async Task<bool> LockVerifiedPhoneAsync(Guid userId, long version, string destination, CancellationToken ct)
    {
        // Held until the transition commits, so a password change or a concurrent phone change
        // serializes on this row rather than overtaking the decision made from it.
        var current = await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        return current is { IsPhoneVerified: true, IsLoginEnabled: true }
            && current.SecurityVersion == version
            && string.Equals(current.PhoneNumber, destination, StringComparison.Ordinal);
    }

    public async Task<bool> HasBusinessHistoryAsync(Guid userId, CancellationToken ct)
    {
        // Serialized with the durable membership-insert marker, and read past the change tracker so
        // an earlier lookup cannot hide a concurrently committed one.
        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .SingleAsync(ct);

        return user.RequiresOperatorRecovery;
    }

    public async Task InvalidateCredentialsAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // users -> auth_sessions -> refresh_tokens, the order every other security transition
        // takes. Acquiring them the other way round here would deadlock against a renewal holding
        // the account row while it touches the session.
        _ = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE id = {userId} FOR UPDATE").ToListAsync(ct);
        await db.AuthSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevocationReason, "credentials-invalidated"), ct);
        await db.RefreshTokens.Where(x => x.UserId == userId).RevokeAsync("credentials-invalidated", now, ct);
        await db.PasswordResetTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public void Add(PhoneChallenge challenge) => db.Add(challenge);

    public void AddSms(IdentitySms message) => db.Add(message);

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
