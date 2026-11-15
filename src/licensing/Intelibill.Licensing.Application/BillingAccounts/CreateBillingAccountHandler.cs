using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

public sealed record CreatedBillingAccount(BillingAccountV1 Account, bool Created);

/// <summary>
/// Explicit creation of a billing account whose only owner is the verified actor. A retry with the same
/// operation key and name answers the same account; the account identifier is the idempotency fence.
/// </summary>
public sealed class CreateBillingAccountHandler(
    IBillingDelegationAccess access, IBillingAccountStore store, TimeProvider clock)
{
    public async Task<ErrorOr<CreatedBillingAccount>> HandleAsync(VerifiedBillingDelegation delegation,
        CreateBillingAccountRequestV1 request, CancellationToken cancellationToken)
    {
        if (request.OperationKey == Guid.Empty)
            return BillingAccountErrors.OperationKeyInvalid;
        if (BillingAccountRules.NormalizeName(request.Name) is not { } name)
            return BillingAccountErrors.NameInvalid;

        return await access.ExecuteAsync(delegation,
            (scope, token) => CreateAsync(scope, request.OperationKey, name, token), cancellationToken);
    }

    private async Task<ErrorOr<CreatedBillingAccount>> CreateAsync(IBillingDelegationScope scope, Guid operationKey,
        string name, CancellationToken cancellationToken)
    {
        var accountId = BillingAccountRules.DeriveAccountId(scope.ActorId, operationKey);
        var hash = BillingAccountRules.RequestHash(name);
        await scope.UseAccountAsync(accountId, cancellationToken);
        var (account, owner) = BillingAccount.Create(accountId, name, scope.ActorId, operationKey, hash,
            BillingAccountRules.StorageInstant(clock.GetUtcNow()));
        if (await store.TryCreateAsync(account, owner, cancellationToken))
            return new CreatedBillingAccount(ToContract(account.Id, account.Name, owner.Role, account.CreatedAt), true);

        var existing = await store.FindAsync(accountId, scope.ActorId, cancellationToken);
        if (existing is null || existing.CreatedByMerchantUserId != scope.ActorId)
            return BillingDelegationErrors.Denied;
        if (!existing.CreationRequestHash.AsSpan().SequenceEqual(hash))
            return BillingAccountErrors.PayloadMismatch;

        return new CreatedBillingAccount(ToContract(existing.Id, existing.Name, existing.Role, existing.CreatedAt), false);
    }

    internal static BillingAccountV1 ToContract(Guid id, string name, BillingAccountRole role, DateTimeOffset createdAt) =>
        new(id, name, BillingAccountRules.RoleName(role), createdAt);
}
