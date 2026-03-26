using InventoryAI.Domain.Entities;

namespace InventoryAI.Domain.Interfaces.Repositories;

public interface IPasswordResetTokenRepository : IRepository<PasswordResetToken>
{
    Task<PasswordResetToken?> GetValidByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
