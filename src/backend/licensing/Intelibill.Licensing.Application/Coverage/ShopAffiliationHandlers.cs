using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public interface IShopAffiliationStore
{
    Task<ErrorOr<ShopAffiliationRequestV1>> RequestAsync(Guid account, Guid actor, RequestShopAffiliationV1 request, CancellationToken ct);
    Task<ErrorOr<ShopAffiliationAcknowledgementV1>> ApplyAsync(ShopAffiliationDecisionV1 decision, string digest,
        Guid replayId, DateTimeOffset expiresAt, CancellationToken ct);
    Task<ErrorOr<AccountAffiliationsV1>> ListAsync(Guid account, CancellationToken ct);
    Task<ErrorOr<ShopAffiliationRequestV1>> ReadForOwnerAsync(Guid account, Guid shop, Guid request, CancellationToken ct);
}

public sealed class ShopAffiliationHandlers(IBillingDelegationAccess access, IShopAffiliationStore store)
{
    public Task<ErrorOr<ShopAffiliationRequestV1>> RequestAsync(VerifiedBillingDelegation delegation,
        Guid account, RequestShopAffiliationV1 request, CancellationToken ct) =>
        access.ExecuteAsync<ShopAffiliationRequestV1>(delegation, (scope, token) => ShopAffiliationPolicy.IsExplicit(request)
            ? store.RequestAsync(account, scope.ActorId, request, token)
            : Task.FromResult<ErrorOr<ShopAffiliationRequestV1>>(ShopAffiliationPolicy.Invalid), ct);

    public Task<ErrorOr<AccountAffiliationsV1>> ListAsync(VerifiedBillingDelegation delegation, Guid account,
        CancellationToken ct) => access.ExecuteAsync<AccountAffiliationsV1>(delegation, (_, token) => store.ListAsync(account, token), ct);

    public Task<ErrorOr<ShopAffiliationRequestV1>> ReadForOwnerAsync(VerifiedBillingDelegation delegation,
        Guid account, Guid shop, Guid request, CancellationToken ct) => access.ExecuteAsync<ShopAffiliationRequestV1>(
        delegation, (_, token) => store.ReadForOwnerAsync(account, shop, request, token), ct);
}
