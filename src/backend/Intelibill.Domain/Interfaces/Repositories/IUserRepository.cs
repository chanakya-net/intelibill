using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<User?> GetByIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneAsync(string phoneNumber, Guid excludedUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A display name (full name, else email, else phone number) per user who holds or held a membership,
    /// of any status, in <paramref name="shopId"/>. Everyone else is absent: a name, email or phone is
    /// never disclosed for an id with no membership in that shop.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
