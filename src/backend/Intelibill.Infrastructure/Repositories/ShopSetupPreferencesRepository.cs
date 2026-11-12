using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ShopSetupPreferencesRepository(ApplicationDbContext db) : IShopSetupPreferencesRepository
{
    public Task<ShopSetupPreferences?> GetAsync(Guid shopId, CancellationToken ct) =>
        db.Set<ShopSetupPreferences>().SingleOrDefaultAsync(p => p.ShopId == shopId, ct);

    public void Add(ShopSetupPreferences preferences) => db.Add(preferences);
}
