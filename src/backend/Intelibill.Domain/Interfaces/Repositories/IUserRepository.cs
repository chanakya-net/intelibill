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
    /// A display name per known user: full name, else email, else phone number. Unknown ids are absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
