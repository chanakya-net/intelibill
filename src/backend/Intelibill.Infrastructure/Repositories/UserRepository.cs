using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class UserRepository(ApplicationDbContext context) : RepositoryBase<User>(context), IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.ToLowerInvariant();
        return await DbSet
            .Include(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public async Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);

    /// <summary>
    /// Loads the account with its <em>live</em> shop memberships.
    /// </summary>
    /// <remarks>
    /// Suspended and removed memberships are filtered out here on purpose. Every caller uses this
    /// collection to answer "may this person act in that shop", including handlers that take a
    /// shop id from the request body rather than the token, so a single filtered load is what
    /// makes suspension effective everywhere instead of relying on each handler to remember.
    /// Membership administration, which must see suspended and removed rows, reads them through
    /// <see cref="Intelibill.Domain.Interfaces.Repositories.IMembershipAuthorityRepository"/>.
    /// </remarks>
    public async Task<User?> GetByIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(u => u.ShopMemberships.Where(sm => sm.Status == MembershipStatus.Active))
            .ThenInclude(sm => sm.Shop)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.ToLowerInvariant();
        return await DbSet.AnyAsync(u => u.Email == normalized, cancellationToken);
    }

    public async Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);
    }

    public async Task<bool> ExistsByPhoneAsync(string phoneNumber, Guid excludedUserId, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(
            u => u.PhoneNumber == phoneNumber && u.Id != excludedUserId,
            cancellationToken);
    }
}
