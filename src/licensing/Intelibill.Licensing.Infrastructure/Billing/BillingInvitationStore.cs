using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class BillingInvitationStore(LicensingDbContext db, BillingInvitationSecrets secrets,
    IBillingAccountStore accounts, TimeProvider clock)
    : IBillingInvitationStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<int> OwnerRevisionAsync(Guid accountId, Guid actorId, CancellationToken ct) =>
        await db.Database.SqlQuery<int>($"""
            SELECT revision AS "Value" FROM public.billing_account_members
            WHERE account_id = {accountId} AND merchant_user_id = {actorId} AND role = 1 AND status = 1
            """).SingleOrDefaultAsync(ct);

    public async Task<ErrorOr<BillingInvitationAcknowledgementV1>> IssueAsync(BillingInvitationIssue command, CancellationToken ct)
    {
        var (account, actor, requestId, request, expiry) = command;
        await LockAccountAsync(account, ct);
        var now = Now();
        if (now >= expiry)
            return BillingDelegationErrors.Invalid;
        if (await OwnerRevisionAsync(account, actor, ct) != request.OwnerRevision)
            return BillingInvitationErrors.RevisionConflict;

        var hash = Convert.FromHexString(request.RequestSha256);
        var previous = await db.Database.SqlQuery<byte[]>($"""
            SELECT request_hash AS "Value" FROM public.billing_invitation_issue_receipts
            WHERE account_id = {account} AND actor_id = {actor} AND operation_key = {request.OperationKey}
            """).SingleOrDefaultAsync(ct);
        var acknowledgement = new BillingInvitationAcknowledgementV1(request.OperationKey);
        if (previous is not null)
            return CryptographicOperations.FixedTimeEquals(previous, hash) ? acknowledgement : BillingAccountErrors.PayloadMismatch;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_invitation_issue_receipts (account_id, actor_id, operation_key, request_hash)
            VALUES ({account}, {actor}, {request.OperationKey}, {hash})
            """, ct);
        if (request.InvitedActorId is { } recipient && recipient != actor &&
            await CanInviteAsync(account, actor, request.OwnerRevision, recipient, now, ct))
            await InsertInvitationAsync(account, actor, requestId, request.OwnerRevision, recipient, now, ct);
        return acknowledgement;
    }

    private async Task<bool> CanInviteAsync(Guid account, Guid actor, int ownerRevision, Guid recipient,
        DateTimeOffset now, CancellationToken ct) => await db.Database.SqlQuery<bool>($"""
        SELECT
            NOT EXISTS (SELECT FROM public.billing_roster({Guid.Empty}, {recipient}))
            AND NOT EXISTS (
                SELECT FROM public.billing_account_invitations
                WHERE account_id = {account} AND invited_actor_id = {recipient} AND issuer_actor_id = {actor}
                  AND issuer_revision = {ownerRevision} AND revision = 1 AND expires_at > {now}
            ) AS "Value"
        """).SingleAsync(ct);

    private async Task InsertInvitationAsync(Guid account, Guid actor, Guid requestId, int ownerRevision,
        Guid recipient, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.CreateVersion7(now);
        var (hash, sealedProof) = secrets.Create(account, id, recipient);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_account_invitations
                (id, account_id, invited_actor_id, issuer_actor_id, issuer_revision, account_name,
                    proof_hash, sealed_proof, created_at, expires_at)
            SELECT {id}, id, {recipient}, {actor}, {ownerRevision}, name, {hash}, {sealedProof}, {now}, {now + Lifetime}
            FROM public.billing_accounts WHERE id = {account}
            """, ct);
        await AuditAsync(id, account, actor, requestId, now, "Issued", 1, null, ct);
    }

    public async Task<BillingInvitationInboxV1> InboxAsync(Guid? after, CancellationToken ct)
    {
        var now = Now();
        var rows = await db.Database.SqlQuery<BillingInvitationV1>($"""
            SELECT id AS invitation_id, account_id, account_name, revision, expires_at
            FROM public.billing_account_invitations WHERE revision = 1 AND created_at <= {now} AND expires_at > {now}
                AND id > {after ?? Guid.Empty}
            ORDER BY id LIMIT {BillingInvitationsV1.PageSize + 1}
            """).ToListAsync(ct);
        var items = rows.Take(BillingInvitationsV1.PageSize).ToArray();
        return new(items, rows.Count > items.Length ? items[^1].InvitationId : null);
    }

    public async Task<BillingRosterV1> RosterAsync(Guid accountId, Guid actorId, Guid? after, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<RosterRow>($"""
            SELECT member_id, role, revision, is_current
            FROM public.billing_roster({after ?? Guid.Empty}, NULL) ORDER BY member_id
            """).ToListAsync(ct);
        var items = rows.Take(BillingInvitationsV1.PageSize).Select(row => new BillingMemberV1(row.MemberId,
            BillingAccountRules.RoleName((Domain.Entities.BillingAccountRole)row.Role), row.Revision, row.IsCurrent)).ToArray();
        var revision = await OwnerRevisionAsync(accountId, actorId, ct);
        var name = await db.Database.SqlQuery<string>($"""
            SELECT name AS "Value" FROM public.billing_accounts WHERE id = {accountId}
            """).SingleAsync(ct);
        return new(name, items, rows.Count > items.Length ? items[^1].MemberId : null, revision == 0 ? null : revision);
    }

    private async Task LockAccountAsync(Guid account, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended({'b' + account.ToString("D")}, 0))
            """, ct);
    }

    private Task<int> AuditAsync(Guid invitation, Guid account, Guid actor, Guid requestId, DateTimeOffset at,
        string action, int revision, Guid? member, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.billing_invitation_audit
            (invitation_id, account_id, actor_id, request_id, occurred_at, action, invitation_revision, member_id)
        VALUES ({invitation}, {account}, {actor}, {requestId}, {at}, {action}, {revision}, {member})
        """, ct);

    private DateTimeOffset Now() => BillingAccountRules.StorageInstant(clock.GetUtcNow());
    private sealed record RosterRow(Guid MemberId, int Role, int Revision, bool IsCurrent);
}
