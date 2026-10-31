using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Intelibill.Infrastructure.Repositories;

internal sealed class EmailOwnershipChallengeRepository(ApplicationDbContext db, IUnitOfWork unitOfWork) : IEmailOwnershipRepository
{
    public async Task<IAsyncDisposable> BeginAsync(CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        // Cross-replica serialization for identity transitions and durable destination budgets.
        // Deliberately coarse until identity traffic warrants partitioned lock ordering.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7463218941)", ct);
        return transaction;
    }
    public Task<EmailOwnershipChallenge?> FindAsync(Guid id, CancellationToken ct) => db.Set<EmailOwnershipChallenge>().FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task SupersedeAsync(string destination, string purpose, Guid? userId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Set<EmailOwnershipChallenge>().Where(x => x.Purpose == purpose && x.ConsumedAt == null &&
            (x.Destination == destination || (userId != null && x.UserId == userId))).ToListAsync(ct);
        foreach (var challenge in existing) challenge.Consume(now);
    }
    public async Task<bool> HasSendBudgetAsync(string destination, DateTimeOffset now, CancellationToken ct)
    {
        // Invitation mail is excluded: it is sent by a shop about someone else, so counting it
        // here would let any owner spend — and so deny — that person's own enrollment, email
        // change and account-recovery budget. Invitations carry their own per-shop ceiling.
        var recent = db.Set<IdentityMail>().Where(x =>
            x.Destination == destination && x.InvitationId == null && x.CreatedAt > now.AddHours(-1));
        return await recent.CountAsync(ct) < 5 && !await recent.AnyAsync(x => x.CreatedAt > now.AddMinutes(-1), ct);
    }
    public async Task<bool> LockCurrentCredentialAsync(Guid userId, long version, string destination, CancellationToken ct)
    {
        // Keep the row lock until proof issuance/redemption commits. Password changes
        // outside the identity advisory lock must serialize on this row too.
        var current = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        return current is { IsEmailVerified: true, IsLoginEnabled: true, PasswordHash: not null }
            && current.SecurityVersion == version && string.Equals(current.Email, destination, StringComparison.OrdinalIgnoreCase);
    }
    public async Task<bool> HasBusinessHistoryAsync(Guid userId, CancellationToken ct)
    {
        // Serialize the eligibility decision with the durable membership-insert marker.
        // AsNoTracking avoids an earlier identity lookup hiding a concurrently committed marker.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        return user.RequiresOperatorRecovery;
    }
    public async Task InvalidateCredentialsAsync(Guid userId, CancellationToken ct)
    {
        await db.RefreshTokens.Where(x => x.UserId == userId && !x.IsRevoked).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true).SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
        await db.PasswordResetTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    }
    public void Add(EmailOwnershipChallenge challenge) => db.Add(challenge);
    public void AddMail(IdentityMail mail) => db.Add(mail);
    public async Task<bool> CommitAsync(CancellationToken ct)
    {
        try { await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { await unitOfWork.RollbackTransactionAsync(ct); return false; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { await unitOfWork.RollbackTransactionAsync(ct); return false; }
    }
}
