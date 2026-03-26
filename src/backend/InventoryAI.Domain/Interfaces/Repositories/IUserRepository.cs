using InventoryAI.Domain.Entities;
using InventoryAI.Domain.Enums;

namespace InventoryAI.Domain.Interfaces.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<User?> GetByExternalLoginAsync(ExternalAuthProvider provider, string providerKey, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
