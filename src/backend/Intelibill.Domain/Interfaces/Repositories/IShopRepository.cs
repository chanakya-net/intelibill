using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IShopRepository : IRepository<Shop>
{
    Task<IReadOnlyList<ShopMembership>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ShopMembership?> GetMembershipAsync(Guid userId, Guid shopId, CancellationToken cancellationToken = default);
    Task<ShopMembership?> GetDefaultMembershipAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ShopMembership?> GetMostRecentlyUsedMembershipAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> UserHasMembershipAsync(Guid userId, Guid shopId, CancellationToken cancellationToken = default);
    Task ClearDefaultForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Shop?> GetByIdWithMembersAsync(Guid shopId, CancellationToken cancellationToken = default);
}