using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IShopSetupPreferencesRepository
{
    Task<ShopSetupPreferences?> GetAsync(Guid shopId, CancellationToken ct);
    void Add(ShopSetupPreferences preferences);
}
