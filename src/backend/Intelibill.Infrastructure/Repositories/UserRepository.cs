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
            .Include(u => u.ExternalLogins)
            .Include(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public async Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        await DbSet
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);

    public async Task<User?> GetByExternalLoginAsync(
        ExternalAuthProvider provider,
        string providerKey,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(u => u.ExternalLogins)
            .Include(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .FirstOrDefaultAsync(
                u => u.ExternalLogins.Any(el => el.Provider == provider && el.ProviderKey == providerKey),
                cancellationToken);

    public async Task<User?> GetByIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(u => u.ExternalLogins)
            .Include(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.ToLowerInvariant();
        return await DbSet.AnyAsync(u => u.Email == normalized, cancellationToken);
    }

    public async Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        await DbSet.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);
}
