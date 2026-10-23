using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class MembershipAuthorityRepository(ApplicationDbContext db, IUnitOfWork unitOfWork)
    : IMembershipAuthorityRepository
{
    public async Task<IAsyncDisposable> BeginAsync(CancellationToken ct) =>
        await db.Database.BeginTransactionAsync(ct);

    public async Task LockUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        // Ascending id order, de-duplicated: the first leg of the authority-lock protocol.
        foreach (var userId in userIds.Distinct().OrderBy(id => id))
        {
            _ = await db.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE id = {userId} FOR UPDATE")
                .ToListAsync(ct);
        }
    }

    public async Task<bool> LockShopAsync(Guid shopId, CancellationToken ct)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM shops WHERE id = {shopId} FOR UPDATE")
            .ToListAsync(ct);
        return locked.Count > 0;
    }

    public async Task<ShopMembership?> LockMembershipAsync(Guid shopId, Guid userId, CancellationToken ct)
    {
        _ = await db.Database
            .SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM shop_memberships WHERE shop_id = {shopId} AND user_id = {userId} FOR UPDATE")
            .ToListAsync(ct);

        return await db.ShopMemberships
            .Include(sm => sm.User)
            .FirstOrDefaultAsync(sm => sm.ShopId == shopId && sm.UserId == userId, ct);
    }

    public Task<User?> FindUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

    public Task<ShopInvitation?> PeekInvitationAsync(Guid invitationId, CancellationToken ct) =>
        db.Set<ShopInvitation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == invitationId, ct);

    public async Task<ShopInvitation?> LockInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        _ = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM shop_invitations WHERE id = {invitationId} FOR UPDATE")
            .ToListAsync(ct);

        return await db.Set<ShopInvitation>().FirstOrDefaultAsync(x => x.Id == invitationId, ct);
    }

    public async Task<IReadOnlyList<ShopInvitation>> GetInvitationsForShopAsync(Guid shopId, CancellationToken ct) =>
        await db.Set<ShopInvitation>()
            .AsNoTracking()
            .Where(x => x.ShopId == shopId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task RevokePendingInvitationsAsync(Guid shopId, string? email, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = email?.Trim().ToLowerInvariant();

        var pending = await db.Set<ShopInvitation>()
            .Where(x => x.ShopId == shopId
                && x.AcceptedAt == null
                && x.RevokedAt == null
                && (normalized == null || x.Email == normalized))
            .ToListAsync(ct);

        foreach (var invitation in pending)
        {
            invitation.Revoke(now);
        }
    }

    public Task<int> CountRecentInvitationsAsync(Guid shopId, DateTimeOffset since, CancellationToken ct) =>
        db.Set<ShopInvitation>().CountAsync(x => x.ShopId == shopId && x.CreatedAt > since, ct);

    public void AddInvitation(ShopInvitation invitation) => db.Add(invitation);

    public async Task AddMembershipAsync(ShopMembership membership, CancellationToken ct) =>
        await db.ShopMemberships.AddAsync(membership, ct);

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
