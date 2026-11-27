using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class BillingInvitationStore
{
    public async Task<ErrorOr<BillingInvitationProofV1>> ProofAsync(IBillingDelegationScope scope, Guid invitationId,
        DateTimeOffset delegationExpiry, CancellationToken ct)
    {
        var row = await AuthorizeInviteeAsync(scope, invitationId, ct);
        var now = Now();
        if (now >= delegationExpiry)
            return BillingDelegationErrors.Invalid;
        if (row is null || !row.IsPendingAt(now))
            return BillingInvitationErrors.Unavailable;

        var token = secrets.Read(row.AccountId, invitationId, scope.ActorId, row.SealedProof!);
        return token is null ? BillingDelegationErrors.StorageUnavailable : new BillingInvitationProofV1(token, row.Revision);
    }

    public async Task<ErrorOr<BillingAccountV1>> AcceptAsync(IBillingDelegationScope scope, Guid invitationId, Guid requestId,
        AcceptBillingInvitationV1 request, DateTimeOffset delegationExpiry, CancellationToken ct)
    {
        var row = await AuthorizeInviteeAsync(scope, invitationId, ct);
        if (row is null)
            return BillingInvitationErrors.Unavailable;

        row = await LockPendingAsync(invitationId, scope.ActorId, ct);
        var now = Now();
        if (now >= delegationExpiry)
            return BillingDelegationErrors.Invalid;
        if (row is null || !row.IsPendingAt(now) || row.Revision != request.ExpectedInvitationRevision ||
            !CryptographicOperations.FixedTimeEquals(row.ProofHash,
                BillingInvitationSecrets.Hash(row.AccountId, invitationId, scope.ActorId, request.Token)))
            return BillingInvitationErrors.Unavailable;

        return await GrantManagerAsync(invitationId, row, scope.ActorId, requestId, now, ct);
    }

    private async Task<PendingRow?> AuthorizeInviteeAsync(IBillingDelegationScope scope, Guid invitationId,
        CancellationToken ct)
    {
        var row = await FindPendingAsync(invitationId, scope.ActorId, ct);
        if (row is null || !row.IsPendingAt(Now()))
            return null;

        await scope.UseAccountAsync(row.AccountId, ct);
        await BillingAccountLock.TakeAsync(db, row.AccountId, ct);
        var current = await db.Database.SqlQuery<bool>($"""
            SELECT public.billing_inviter_is_current({invitationId}) AS "Value"
            """).SingleAsync(ct);
        return current ? row : null;
    }

    private async Task<PendingRow?> FindPendingAsync(Guid invitationId, Guid actorId, CancellationToken ct) =>
        await db.Database.SqlQuery<PendingRow>($"""
            SELECT account_id, revision, proof_hash, sealed_proof, created_at, expires_at
            FROM public.billing_account_invitations
            WHERE id = {invitationId} AND invited_actor_id = {actorId} AND revision = 1
            """).SingleOrDefaultAsync(ct);

    private async Task<PendingRow?> LockPendingAsync(Guid invitationId, Guid actorId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<PendingRow>($"""
            SELECT account_id, revision, proof_hash, sealed_proof, created_at, expires_at
            FROM public.billing_account_invitations
            WHERE id = {invitationId} AND invited_actor_id = {actorId} AND revision = 1
            FOR UPDATE
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private async Task<ErrorOr<BillingAccountV1>> GrantManagerAsync(Guid invitationId, PendingRow row,
        Guid actor, Guid requestId, DateTimeOffset now, CancellationToken ct)
    {
        var member = Guid.CreateVersion7(now);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public.billing_account_invitations SET revision = 2, accepted_at = {now},
                accepted_member_id = {member}, sealed_proof = NULL WHERE id = {invitationId} AND revision = {row.Revision}
            """, ct);
        if (changed != 1)
            return BillingInvitationErrors.Unavailable;

        var created = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_account_members
                (id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES ({member}, {row.AccountId}, {actor}, 2, 1, 1, {now})
            ON CONFLICT (account_id, merchant_user_id) WHERE status = 1 DO NOTHING
            """, ct);
        if (created != 1)
            return BillingInvitationErrors.Unavailable;

        await AuditAsync(invitationId, row.AccountId, actor, requestId, now, "Accepted", 2, member, ct);
        var account = await accounts.FindAsync(row.AccountId, actor, ct);
        return account is null ? BillingDelegationErrors.StorageUnavailable
            : new BillingAccountV1(row.AccountId, account.Name, MerchantBillingDelegationV1.ManagerRole, account.CreatedAt);
    }

    private sealed record PendingRow(Guid AccountId, int Revision, byte[] ProofHash, string? SealedProof,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
    {
        internal bool IsPendingAt(DateTimeOffset now) => SealedProof is not null && CreatedAt <= now && now < ExpiresAt;

        public override string ToString() => "[REDACTED]";
    }
}
