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
    public Task<ErrorOr<BillingAccountListV1>> HandleAsync(VerifiedBillingDelegation delegation,
        CancellationToken cancellationToken) =>
        access.ExecuteAsync(delegation, ListAsync, cancellationToken);

    private async Task<ErrorOr<BillingAccountListV1>> ListAsync(IBillingDelegationScope scope,
        CancellationToken cancellationToken)
    {
        var ids = await store.ListActorAccountIdsAsync(scope.ActorId,
            MerchantBillingDelegationV1.MaximumListedAccounts, cancellationToken);
        var accounts = new List<BillingAccountV1>(ids.Count);
        foreach (var id in ids)
        {
            await scope.UseAccountAsync(id, cancellationToken);
            // A membership the actor can see must name an account it can read; anything else fails closed.
            if (await store.FindAsync(id, scope.ActorId, cancellationToken) is not { } account)
                return BillingDelegationErrors.StorageUnavailable;
            accounts.Add(CreateBillingAccountHandler.ToContract(account.Id, account.Name, account.Role, account.CreatedAt));
        }
        return new BillingAccountListV1([.. accounts]);
    }
}
