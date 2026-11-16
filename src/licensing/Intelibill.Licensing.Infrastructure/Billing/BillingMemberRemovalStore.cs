using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed class BillingMemberRemovalStore(LicensingDbContext db, TimeProvider clock) : IBillingMemberRemovalStore
{
    public async Task<ErrorOr<BillingManagerRemovalV1>> RemoveAsync(BillingMemberRemoval command, CancellationToken ct)
    {
        var target = await LockTargetAsync(command, ct);
        if (clock.GetUtcNow() >= command.ExpiresAt)
            return BillingDelegationErrors.Invalid;

        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.MemberId, command.ExpectedVersion, command.Reason,
        }));
        var previous = await FindReceiptAsync(command, ct);
        var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
        if (clock.GetUtcNow() >= command.ExpiresAt)
            return BillingDelegationErrors.Invalid;
        if (previous is not null)
            return CryptographicOperations.FixedTimeEquals(previous.RequestHash, hash)
                ? Answer(command, previous.AfterRevision, previous.RemovedAt) : BillingAccountErrors.PayloadMismatch;
        if (target is null)
            return BillingMemberRemovalErrors.Unavailable;
        if (target.Role == (int)BillingAccountRole.Owner)
            return BillingMemberRemovalErrors.OwnerForbidden;
        if (target.Status != (int)BillingAccountMemberStatus.Active || target.Revision != command.ExpectedVersion)
            return BillingMemberRemovalErrors.VersionConflict;

        return await ApplyAsync(command, hash, now, ct);
    }

    private Task<TargetRow?> LockTargetAsync(BillingMemberRemoval command, CancellationToken ct) =>
        db.Database.SqlQuery<TargetRow>($"""
            SELECT role, status, revision FROM public.lock_billing_removal_target({command.MemberId})
            """).SingleOrDefaultAsync(ct);

    private Task<ReceiptRow?> FindReceiptAsync(BillingMemberRemoval command, CancellationToken ct) =>
        db.Database.SqlQuery<ReceiptRow>($"""
            SELECT request_hash, after_revision, removed_at FROM public.billing_member_removals
            WHERE account_id = {command.AccountId} AND actor_id = {command.ActorId}
                AND operation_key = {command.OperationKey}
            """).SingleOrDefaultAsync(ct);

    private async Task<ErrorOr<BillingManagerRemovalV1>> ApplyAsync(BillingMemberRemoval command, byte[] hash,
        DateTimeOffset now, CancellationToken ct)
    {
        var applied = await db.Database.SqlQuery<bool>($"""
            SELECT public.remove_billing_manager({command.MemberId}, {command.ExpectedVersion},
                {command.OperationKey}, {command.RequestId}, {hash}, {command.Reason}, {now}) AS "Value"
            """).SingleAsync(ct);
        return applied ? Answer(command, command.ExpectedVersion + 1, now) : BillingMemberRemovalErrors.VersionConflict;
    }

    private static BillingManagerRemovalV1 Answer(BillingMemberRemoval command, int revision, DateTimeOffset at) =>
        new(command.AccountId, command.MemberId, command.OperationKey, "Removed", revision, at);

    private sealed record TargetRow(int Role, int Status, int Revision);
    private sealed record ReceiptRow(byte[] RequestHash, int AfterRevision, DateTimeOffset RemovedAt);
}
