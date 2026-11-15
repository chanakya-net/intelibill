using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class PostgresBillingDelegationAccess(
    LicensingDbContext db, IBillingAccountDelegationAuthority authority, TimeProvider clock,
    ILogger<PostgresBillingDelegationAccess> logger) : IBillingDelegationAccess
{
    public Task<ErrorOr<BillingAccountAccessV1>> AuthorizeAsync(
        VerifiedBillingDelegation delegation, CancellationToken cancellationToken)
    {
        if (delegation.AccountId is not { } account || delegation.Action != MerchantBillingDelegationV1.ReadAccount)
            return Task.FromResult<ErrorOr<BillingAccountAccessV1>>(BillingDelegationErrors.Invalid);

        return ExecuteAsync(delegation, (_, _) => Task.FromResult<ErrorOr<BillingAccountAccessV1>>(
            new BillingAccountAccessV1(account, [delegation.Action])), cancellationToken);
    }

    public async Task<ErrorOr<T>> ExecuteAsync<T>(VerifiedBillingDelegation delegation,
        Func<IBillingDelegationScope, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteInTransactionAsync(delegation, operation, cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException
            || exception is DbUpdateException { InnerException: NpgsqlException })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cause = exception is DbUpdateException ? exception.InnerException! : exception;
            LogStorageUnavailable(logger, cause.GetType().Name);
            return BillingDelegationErrors.StorageUnavailable;
        }
    }

    private async Task<ErrorOr<T>> ExecuteInTransactionAsync<T>(VerifiedBillingDelegation delegation,
        Func<IBillingDelegationScope, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Transaction-local: commit or rollback clears the scope before the pooled connection is reused.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.billing_account_id', {delegation.AccountId?.ToString("D") ?? string.Empty}, true),
                   set_config('app.billing_actor_id', {delegation.ActorId.ToString("D")}, true)
            """, cancellationToken);
        var grant = await AuthorizeScopeAsync(delegation, cancellationToken);
        if (grant.IsError)
        {
            if (grant.FirstError == BillingDelegationErrors.MembershipUnavailable)
                LogMembershipUnavailable(logger);
            return grant.Errors;
        }
        var consumedAt = clock.GetUtcNow();
        if (consumedAt < delegation.IssuedAt || consumedAt >= delegation.ExpiresAt)
            return BillingDelegationErrors.Invalid;

        var inserted = await ConsumeAsync(delegation, consumedAt, cancellationToken);
        // A competing use may have held the unique index entry until after expiry.
        if (clock.GetUtcNow() >= delegation.ExpiresAt)
            return BillingDelegationErrors.Invalid;
        if (inserted != 1)
            return BillingDelegationErrors.Replayed;

        var result = await operation(new Scope(db, delegation), cancellationToken);
        if (result.IsError)
            return result.Errors;

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    // Account-scoped actions need live membership. Actor-scoped actions are the explicit pre-membership
    // policy: only the named actions, for an actor whose verified identity the assertion states.
    private Task<ErrorOr<Success>> AuthorizeScopeAsync(VerifiedBillingDelegation delegation, CancellationToken cancellationToken)
    {
        var actorScoped = MerchantBillingDelegationV1.IsActorScoped(delegation.Action);
        if (delegation.AccountId is { } account)
            return actorScoped
                ? Task.FromResult<ErrorOr<Success>>(BillingDelegationErrors.Invalid)
                : authority.AuthorizeAsync(account, delegation.ActorId, delegation.Action, cancellationToken);

        return Task.FromResult<ErrorOr<Success>>(actorScoped ? Result.Success : BillingDelegationErrors.Invalid);
    }

    private Task<int> ConsumeAsync(VerifiedBillingDelegation proof, DateTimeOffset consumedAt, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.merchant_billing_delegation_uses
                (issuer, replay_id, account_id, actor_id, action, request_id, key_id, issued_at, expires_at, consumed_at)
            VALUES ({MerchantBillingDelegationV1.Issuer}, {proof.ReplayId}, {proof.AccountId}, {proof.ActorId},
                {proof.Action}, {proof.RequestId}, {proof.KeyId}, {proof.IssuedAt}, {proof.ExpiresAt}, {consumedAt})
            ON CONFLICT (issuer, replay_id) DO NOTHING
            """, cancellationToken);

    private sealed class Scope(LicensingDbContext db, VerifiedBillingDelegation delegation) : IBillingDelegationScope
    {
        public Guid ActorId => delegation.ActorId;

        public async Task UseAccountAsync(Guid accountId, CancellationToken cancellationToken)
        {
            if (delegation.AccountId is not null || accountId == Guid.Empty)
                throw new InvalidOperationException("Only an actor-scoped delegation may choose its account context.");

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_account_id', {accountId.ToString("D")}, true)", cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Licensing billing delegation failed (StorageUnavailable); cause={Cause}.")]
    private static partial void LogStorageUnavailable(ILogger logger, string cause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Licensing billing delegation failed (MembershipUnavailable).")]
    private static partial void LogMembershipUnavailable(ILogger logger);
}
