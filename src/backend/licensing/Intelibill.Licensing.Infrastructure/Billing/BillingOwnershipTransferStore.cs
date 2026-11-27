using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class BillingOwnershipTransferStore(LicensingDbContext db, TimeProvider clock)
    : IBillingOwnershipTransferStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private DateTimeOffset Now => BillingAccountRules.StorageInstant(clock.GetUtcNow());

    public async Task<ErrorOr<BillingOwnershipAcknowledgementV1>> IssueAsync(VerifiedBillingDelegation proof,
        Guid account, IssueBillingOwnershipTransferV1 request, CancellationToken ct)
    {
        var now = Now;
        var hash = Convert.FromHexString(request.RequestSha256);
        var saved = await db.Database.SqlQuery<bool>($"""
            SELECT public.issue_billing_ownership_transfer({Guid.CreateVersion7(now)}, {request.RecipientActorId},
                {request.ExpectedVersion}, {request.OperationKey}, {proof.RequestId}, {hash}, {request.Reason},
                {BillingOwnershipTransfersV1.Policy}, {now}, {now + Lifetime}) AS "Value"
            """).SingleAsync(ct);
        if (Now >= proof.ExpiresAt)
            return BillingDelegationErrors.Invalid;
        if (!saved)
            return BillingOwnershipTransferErrors.Stale;

        var previous = await db.Database.SqlQuery<byte[]>($"""
            SELECT request_hash AS "Value" FROM public.billing_ownership_proposals
            WHERE account_id = {account} AND owner_actor_id = {proof.ActorId} AND operation_key = {request.OperationKey}
            """).SingleOrDefaultAsync(ct);
        return previous is not null && CryptographicOperations.FixedTimeEquals(previous, hash)
            ? new BillingOwnershipAcknowledgementV1(request.OperationKey) : BillingAccountErrors.PayloadMismatch;
    }

    public async Task<BillingOwnershipInboxV1> InboxAsync(Guid? after, CancellationToken ct)
    {
        var rows = await Proposals(Now).Where(row => row.TransferId.CompareTo(after ?? Guid.Empty) > 0)
            .OrderBy(row => row.TransferId).Take(BillingOwnershipTransfersV1.PageSize + 1).ToListAsync(ct);
        var items = rows.Take(BillingOwnershipTransfersV1.PageSize).ToArray();
        return new(items, rows.Count > items.Length ? items[^1].TransferId : null);
    }

    public async Task<ErrorOr<BillingOwnershipProposalV1>> PreviewAsync(Guid transfer, CancellationToken ct) =>
        await Proposals(Now).SingleOrDefaultAsync(row => row.TransferId == transfer, ct) is { } row
            ? row : BillingOwnershipTransferErrors.Missing;

    private IQueryable<BillingOwnershipProposalV1> Proposals(DateTimeOffset now) =>
        db.Database.SqlQuery<BillingOwnershipProposalV1>($"""
            SELECT id AS transfer_id, account_id, account_name, owner_actor_id, recipient_actor_id,
                   1 AS revision, responsibility_policy_version, expires_at
            FROM public.billing_ownership_proposals p
            WHERE recipient_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                AND created_at <= {now} AND {now} < expires_at
                AND NOT EXISTS (SELECT FROM public.billing_ownership_acceptances a WHERE a.transfer_id = p.id)
            """);

    public async Task<ErrorOr<BillingOwnershipAcknowledgementV1>> RecoverIssueAsync(Guid key, CancellationToken ct) =>
        await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.billing_ownership_proposals
                WHERE owner_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                  AND operation_key = {key}) AS "Value"
            """).SingleAsync(ct) ? new BillingOwnershipAcknowledgementV1(key) : BillingOwnershipTransferErrors.Missing;

}
