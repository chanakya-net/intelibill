using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public async Task<ErrorOr<ShopSubscriptionStatusV1>> ReadShopAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct)
    {
        try
        {
            // Live shop scope before signing, and again before releasing the answer (a switch may land in flight).
            if (!await IsLiveShopMemberAsync(caller, shop, ct))
                return MerchantSubscriptionStatusErrors.ShopScope;
            var result = await RunAsync<ShopSubscriptionStatusV1>(caller,
                new(SubscriptionStatusV1.ShopRead, HttpMethod.Get, SubscriptionStatusV1.ShopPath(shop), null, []),
                SubscriptionStatusV1.ShopResponseLimit, async (response, token) =>
                {
                    var status = await ReadJsonAsync<ShopSubscriptionStatusV1>(response, token);
                    return status is not null && SubscriptionStatusContract.IsValidShop(status, shop)
                        ? status : Unavailable("InvalidStatusContract");
                }, ct);
            if (result.IsError) return result;
            var heads = await SuspensionAccessReader.ReadHeadsAsync(db, shop, ct);
            if (!await IsLiveShopMemberAsync(caller, shop, ct))
                return MerchantSubscriptionStatusErrors.ShopScope;
            var local = heads?.Shop;
            var activated = local?.EnforcementActivatedAt is not null;
            var fullReopening = result.Value.ClosedAt is null;
            var required = result.Value.ReopeningRequiredRevision;
            bool? received = required.HasValue ? local is not null && local.ClosureRevision == required &&
                (local.State != ShopClosuresV1.Closed) == fullReopening : null;
            var statusWithReopening = result.Value with
            {
                ReopeningReceived = received,
                ReopeningEnforced = activated && fullReopening ? received : null,
                ReopenedVersion = received == true && fullReopening ? required : null,
                ReopeningRequiredRevision = null,
            };
            return heads is not null && activated
                ? statusWithReopening with
                {
                    ObserveOnly = false,
                    ClosureEnforced = result.Value.ClosedAt is null ? null : heads.Shop.State == ShopClosuresV1.Closed,
                    EnforcedSuspensionMode = LicenseProjectionPolicy.EffectiveSuspension(heads.Shop, heads.Account, clock.GetUtcNow()),
                }
                : statusWithReopening;
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("StatusScopeUnavailable", error);
        }
    }

    public Task<ErrorOr<AccountSubscriptionStatusV1>> ReadAccountAsync(MerchantBillingCaller caller, Guid account, Guid? after,
        CancellationToken ct)
    {
        if (account == Guid.Empty || after == Guid.Empty)
            return Task.FromResult<ErrorOr<AccountSubscriptionStatusV1>>(MerchantBillingErrors.Denied);
        return RunAsync<AccountSubscriptionStatusV1>(caller, new(SubscriptionStatusV1.AccountRead, HttpMethod.Get,
            SubscriptionStatusV1.AccountPath(account, after), account, []), SubscriptionStatusV1.AccountResponseLimit,
            async (response, token) =>
            {
                var status = await ReadJsonAsync<AccountSubscriptionStatusV1>(response, token);
                return status is not null && SubscriptionStatusContract.IsValidAccount(status, account, after)
                    ? status : Unavailable("InvalidStatusContract");
            }, ct);
    }

    private Task<bool> IsLiveShopMemberAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct) =>
        caller.Session.ActiveShopId != shop || shop == Guid.Empty ? Task.FromResult(false)
            : db.ShopMemberships.AsNoTracking().AnyAsync(m => m.ShopId == shop && m.UserId == caller.Session.UserId
                && m.Status == MembershipStatus.Active, ct);
}
