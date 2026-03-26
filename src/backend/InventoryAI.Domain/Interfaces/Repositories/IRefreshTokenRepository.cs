using InventoryAI.Domain.Entities;

namespace InventoryAI.Domain.Interfaces.Repositories;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetActiveByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
