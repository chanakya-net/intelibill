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
    public async Task<ErrorOr<BillingAccountAccessV1>> AuthorizeAsync(
        VerifiedBillingDelegation delegation, CancellationToken cancellationToken)
    {
        try
        {
            return await AuthorizeInTransactionAsync(delegation, cancellationToken);
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

    private async Task<ErrorOr<BillingAccountAccessV1>> AuthorizeInTransactionAsync(
        VerifiedBillingDelegation delegation, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.billing_account_id', {delegation.AccountId.ToString("D")}, true),
                   set_config('app.billing_actor_id', {delegation.ActorId.ToString("D")}, true)
            """, cancellationToken);
        var grant = await authority.AuthorizeAsync(
            delegation.AccountId, delegation.ActorId, delegation.Action, cancellationToken);
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

        await transaction.CommitAsync(cancellationToken);
        return new BillingAccountAccessV1(delegation.AccountId, [delegation.Action]);
    }

    private Task<int> ConsumeAsync(VerifiedBillingDelegation proof, DateTimeOffset consumedAt, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.merchant_billing_delegation_uses
                (issuer, replay_id, account_id, actor_id, action, request_id, key_id, issued_at, expires_at, consumed_at)
            VALUES ({MerchantBillingDelegationV1.Issuer}, {proof.ReplayId}, {proof.AccountId}, {proof.ActorId},
                {proof.Action}, {proof.RequestId}, {proof.KeyId}, {proof.IssuedAt}, {proof.ExpiresAt}, {consumedAt})
            ON CONFLICT (issuer, replay_id) DO NOTHING
            """, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Licensing billing delegation failed (StorageUnavailable); cause={Cause}.")]
    private static partial void LogStorageUnavailable(ILogger logger, string cause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Licensing billing delegation failed (MembershipUnavailable).")]
    private static partial void LogMembershipUnavailable(ILogger logger);
}
