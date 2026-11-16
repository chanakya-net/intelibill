using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

/// <summary>
/// Provider status reads. A live, MFA-enrolled administrator on a live session needs the persisted Support,
/// Billing or Licensing Manager grant (re-read under lock); commercial facts need Billing or Licensing Manager.
/// Platform Owner and Security Administrator alone grant nothing here. No merchant business data is read.
/// </summary>
public sealed class ReadProviderStatusHandler(ISubscriptionStatusStore store, ISubscriptionAccessFactsReader reader,
    TimeProvider statusClock, TimeProvider sessionClock)
{
    private const AdminCapability Readers = AdminCapability.Support | AdminCapability.Billing | AdminCapability.LicensingManager;
    private const AdminCapability Commercial = AdminCapability.Billing | AdminCapability.LicensingManager;

    public Task<ErrorOr<AccountSubscriptionStatusV1>> ReadAccountAsync(AdminSessionCaller caller, Guid account, Guid? after,
        CancellationToken ct) => store.InProviderTransactionAsync<AccountSubscriptionStatusV1>(async token =>
    {
        if (account == Guid.Empty || after == Guid.Empty) return SubscriptionStatusErrors.ShopNotCovered;
        var grant = await AuthorizeAsync(caller, token);
        if (grant.IsError) return grant.Errors;
        await store.UseAccountAsync(account, token);
        var rows = await reader.ReadAccountAsync(account, after, SubscriptionStatusV1.PageSize + 1, token);
        return SubscriptionStatusProjection.Account(account, SubscriptionStatusProjection.EvaluationInstant(statusClock),
            rows, grant.Value);
    }, ct);

    public Task<ErrorOr<ShopSubscriptionStatusV1>> ReadShopAsync(AdminSessionCaller caller, Guid account, Guid shop,
        CancellationToken ct) => store.InProviderTransactionAsync<ShopSubscriptionStatusV1>(async token =>
    {
        if (account == Guid.Empty || shop == Guid.Empty) return SubscriptionStatusErrors.ShopNotCovered;
        var grant = await AuthorizeAsync(caller, token);
        if (grant.IsError) return grant.Errors;
        await store.UseAccountAsync(account, token);
        var coverage = await reader.ReadShopAsync(shop, token);
        if (coverage is null || coverage.AccountId != account) return SubscriptionStatusErrors.ShopNotCovered;
        return SubscriptionStatusProjection.Shop(shop, SubscriptionStatusProjection.EvaluationInstant(statusClock),
            coverage, grant.Value);
    }, ct);

    /// <summary>Live authority under the identity → session lock order; the value says whether commercial facts are allowed.</summary>
    private async Task<ErrorOr<bool>> AuthorizeAsync(AdminSessionCaller caller, CancellationToken ct)
    {
        var (actor, session) = await store.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if ((actor.Capabilities & Readers) == 0) return SubscriptionStatusErrors.ProviderDenied;
        return (actor.Capabilities & Commercial) != 0;
    }
}
