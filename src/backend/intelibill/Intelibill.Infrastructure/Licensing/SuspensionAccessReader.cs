using System.Data;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Billing;
using Intelibill.Application.Features.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class SuspensionAccessReader(ApplicationDbContext db, ILicenceActionPolicy policy, TimeProvider clock,
    ITenantWriteScope scope, IServiceScopeFactory services) : ISuspensionAccessReader
{
    public Task<Error?> ReadActiveShopDenialAsync(Guid shopId, LicenceAction action, CancellationToken ct) =>
        ReadScopedDenialAsync(db, shopId, action, ct);

    public async Task<Error?> ReadDenialAsync(SessionClaims session, Guid shopId, LicenceAction action, CancellationToken ct)
    {
        if (session.ActiveShopId == shopId)
            return await ReadActiveShopDenialAsync(shopId, action, ct);

        // Check the explicitly requested shop's live membership before changing RLS scope.
        var isMember = await db.ShopMemberships.AnyAsync(row => row.UserId == session.UserId && row.ShopId == shopId &&
            row.Status == MembershipStatus.Active, ct);
        if (!isMember) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await scope.EnterAsync(shopId, ct);
        return await ReadActiveShopDenialAsync(shopId, action, ct);
    }

    public async Task<Error?> DeliverAsync(Guid shopId, SessionClaims? session, Func<CancellationToken, Task> deliver, CancellationToken ct,
        LicenceAction action = LicenceAction.History, Func<CancellationToken, Task>? prepare = null,
        OwnerResponseAuthority? owner = null)
    {
        // Neither a source transaction nor a streaming query can share this connection.
        await using var reader = services.CreateAsyncScope();
        var provider = reader.ServiceProvider;
        var context = provider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (session is not null && await ReadAuthorityDenialAsync(provider, context, session, shopId, ct) is { } denied)
            return denied;

        await provider.GetRequiredService<ITenantWriteScope>().EnterAsync(shopId, ct);
        var lease = await provider.GetRequiredService<IShopLicenceFence>().EnterAsync(shopId, action, ct);
        if (lease.IsError)
            return lease.FirstError;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        if (session is not null &&
            await ReadAuthorityDenialAsync(provider, context, session, shopId, budget.Token) is { } revoked)
            return revoked;

        if (owner is not null && (session is null ||
            await provider.GetRequiredService<ShopAffiliationConsentStore>().LockOwnerAsync(
                new MerchantBillingCaller(session, owner.AccessExpiry), shopId, budget.Token) is not { } live ||
            live.Version != owner.MembershipVersion))
            return ShopAffiliationConsentErrors.OwnerRequired;

        if (lease.Value.CurrentDecision() is { Enforced: true, Denial: { } refusal })
            return refusal;

        if (prepare is not null)
        {
            await prepare(budget.Token);
            // A scheduled restriction may become effective while live recipient authority is read.
            if (lease.Value.CurrentDecision() is { Enforced: true, Denial: { } effective })
                return effective;
        }

        await deliver(budget.Token);
        return null;
    }

    private static async Task<Error?> ReadAuthorityDenialAsync(IServiceProvider provider,
        ApplicationDbContext context, SessionClaims session, Guid shopId, CancellationToken ct)
    {
        if (!await provider.GetRequiredService<ILiveSessionAuthority>().IsValidAsync(session, ct))
            return Errors.Auth.SessionInvalid;
        if (session.ActiveShopId == shopId || await context.ShopMemberships.AnyAsync(row =>
            row.UserId == session.UserId && row.ShopId == shopId && row.Status == MembershipStatus.Active, ct))
            return null;
        return Errors.Shop.AccessDenied;
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
