using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class SuspensionAccessReader(ApplicationDbContext db, ILicenceActionPolicy policy, TimeProvider clock,
    ITenantWriteScope scope, IServiceScopeFactory services) : ISuspensionAccessReader
{
    public async Task<Error?> ReadDenialAsync(SessionClaims session, Guid shopId, LicenceAction action, CancellationToken ct)
    {
        if (session.ActiveShopId == shopId)
            return await ReadScopedDenialAsync(db, shopId, action, ct);

        // Check the explicitly requested shop's live membership before changing RLS scope.
        var isMember = await db.ShopMemberships.AnyAsync(row => row.UserId == session.UserId && row.ShopId == shopId &&
            row.Status == MembershipStatus.Active, ct);
        if (!isMember) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await scope.EnterAsync(shopId, ct);
        return await ReadScopedDenialAsync(db, shopId, action, ct);
    }

    public async Task<bool> CanReceiveRealtimeAsync(Guid shopId, CancellationToken ct)
    {
        // A notifier can run outside a request or while its publisher owns a transaction.
        await using var reader = services.CreateAsyncScope();
        var context = reader.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await reader.ServiceProvider.GetRequiredService<ITenantWriteScope>().EnterAsync(shopId, ct);
        return await ReadScopedDenialAsync(context, shopId, LicenceAction.History, ct) is null;
    }

    private async Task<Error?> ReadScopedDenialAsync(ApplicationDbContext context, Guid shopId, LicenceAction action, CancellationToken ct)
    {
        var heads = await ReadHeadsAsync(context, shopId, ct);
        var now = clock.GetUtcNow();
        return heads is { Shop.EnforcementActivatedAt: not null } &&
            LicenseProjectionPolicy.EffectiveSuspension(heads.Shop, heads.Account, now) is not null
            ? policy.Decide(action, heads.Shop, heads.Account, null, now).Denial : null;
    }

    internal static Task<Heads?> ReadHeadsAsync(ApplicationDbContext db, Guid shopId, CancellationToken ct) =>
        (from shop in db.Set<ShopLicenseState>().AsNoTracking()
         join account in db.Set<AccountLicenseRestriction>().AsNoTracking() on shop.AccountId equals account.AccountId
         where shop.ShopId == shopId
         select new Heads(shop, account)).SingleOrDefaultAsync(ct);

    internal sealed record Heads(ShopLicenseState Shop, AccountLicenseRestriction Account);
}
