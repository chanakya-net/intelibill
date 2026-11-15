using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

/// <summary>
/// The verified actor's billing accounts: its own live membership index, then each account read under
/// that account's context. Nothing else (shop, email, GSTIN or login) ever adds an account to the list.
/// </summary>
public sealed class ListBillingAccountsHandler(IBillingDelegationAccess access, IBillingAccountStore store)
{
    public Task<ErrorOr<BillingAccountListV1>> HandleAsync(VerifiedBillingDelegation delegation, Guid? after,
        CancellationToken cancellationToken) =>
        access.ExecuteAsync(delegation, (scope, token) => ListAsync(scope, after, token), cancellationToken);

    private async Task<ErrorOr<BillingAccountListV1>> ListAsync(IBillingDelegationScope scope, Guid? after,
        CancellationToken cancellationToken)
    {
        var ids = await store.ListActorAccountIdsAsync(scope.ActorId, after,
            MerchantBillingDelegationV1.MaximumListedAccounts + 1, cancellationToken);
        var accounts = new List<BillingAccountV1>(ids.Count);
        foreach (var id in ids.Take(MerchantBillingDelegationV1.MaximumListedAccounts))
        {
            await scope.UseAccountAsync(id, cancellationToken);
            // A membership the actor can see must name an account it can read; anything else fails closed.
            if (await store.FindAsync(id, scope.ActorId, cancellationToken) is not { } account)
                return BillingDelegationErrors.StorageUnavailable;
            accounts.Add(CreateBillingAccountHandler.ToContract(account.Id, account.Name, account.Role, account.CreatedAt));
        }
        return new BillingAccountListV1([.. accounts],
            ids.Count > MerchantBillingDelegationV1.MaximumListedAccounts ? accounts[^1].AccountId : null);
    }
}
