using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class SuspensionAccessReader(ApplicationDbContext db, ILicenceActionPolicy policy, TimeProvider clock,
    ITenantWriteScope scope) : ISuspensionAccessReader
{
    public async Task<Error?> ReadDenialAsync(SessionClaims session, Guid shopId, LicenceAction action, CancellationToken ct)
    {
        if (session.ActiveShopId == shopId)
            return await ReadScopedDenialAsync(shopId, action, ct);

        // Some explicit-shop routes work from another active shop. Check the requested shop's live
        // membership before changing RLS scope and let the route handle callers without that membership.
        var isMember = await db.ShopMemberships.AnyAsync(row => row.UserId == session.UserId && row.ShopId == shopId &&
            row.Status == MembershipStatus.Active, ct);
        if (!isMember) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await scope.EnterAsync(shopId, ct);
        return await ReadScopedDenialAsync(shopId, action, ct);
    }

    private async Task<Error?> ReadScopedDenialAsync(Guid shopId, LicenceAction action, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var heads = await (from shop in db.Set<ShopLicenseState>().AsNoTracking()
            join account in db.Set<AccountLicenseRestriction>().AsNoTracking() on shop.AccountId equals account.AccountId
            where shop.ShopId == shopId && shop.EnforcementActivatedAt != null &&
                (shop.SuspensionEffectiveAt <= now || account.SuspensionEffectiveAt <= now)
            select new { Shop = shop, Account = account }).SingleOrDefaultAsync(ct);

        return heads is not null && LicenseProjectionPolicy.EffectiveSuspension(heads.Shop, heads.Account, now) is not null
            ? policy.Decide(action, heads.Shop, heads.Account, null, now).Denial : null;
    }
}
