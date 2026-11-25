using ErrorOr;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Shared.Contracts.V1.CoverageTransfersV1;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageTransferStore
{
    private static readonly string[] Purposes = [SourceBillingOwner, DestinationBillingOwner, ShopOwner];

    public Task<Guid?> CounterpartyAsync(Guid account, Guid transfer, CancellationToken ct) =>
        db.Database.SqlQuery<Guid?>($"""
            SELECT CASE WHEN source_account_id = {account} THEN destination_account_id ELSE source_account_id END AS "Value"
            FROM public.coverage_transfer_proposals WHERE transfer_id = {transfer}
            """).SingleOrDefaultAsync(ct);

    public async Task<ErrorOr<CoverageTransferListV1>> ListAsync(Guid account, Guid actor, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT transfer_id AS "Value" FROM public.coverage_transfer_proposals
            WHERE {account} IN (source_account_id, destination_account_id)
            ORDER BY created_at DESC, transfer_id LIMIT {PageSize + 1}
            """).ToArrayAsync(ct);
        var items = new List<CoverageTransferV1>(PageSize);
        foreach (var id in ids.Take(PageSize))
            if (await ViewAsync(id, account, actor, now, ct) is { IsError: false, Value: { } view })
                items.Add(view);
        return new CoverageTransferListV1(account, now, [.. items], ids.Length > PageSize);
    }

    public Task<ErrorOr<CoverageTransferV1>> ReadAsync(Guid account, Guid actor, Guid transfer, CancellationToken ct) =>
        ViewAsync(transfer, account, actor, EntitlementSnapshotsV1.Instant(clock), ct);

    public async Task<ErrorOr<CoverageTransferOwnerTermsV1>> OwnerReadAsync(Guid source, Guid shop, Guid transfer, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('app.coverage_transfer_id', {transfer.ToString("D")}, true)", ct);
        var now = EntitlementSnapshotsV1.Instant(clock);
        if (await ProposalAsync(transfer, now, ct) is not { } p || p.SourceAccountId != source || p.ShopId != shop)
            return CoverageTransferErrors.NotFound;
        return new CoverageTransferOwnerTermsV1(
            Project(p, await ConsentsAsync(transfer, ct), viewerAccount: null, viewer: null, null), p.SourceAffiliationId);
    }

    /// <summary>The narrow participant projection. The viewer must be a live Billing Owner of one of the two parties.</summary>
    private async Task<ErrorOr<CoverageTransferV1>> ViewAsync(Guid transfer, Guid account, Guid actor, DateTimeOffset now,
        CancellationToken ct)
    {
        if (await ProposalAsync(transfer, now, ct) is not { } p || (p.SourceAccountId != account && p.DestinationAccountId != account))
            return CoverageTransferErrors.NotFound;
        var owner = await OwnerAsync(account, actor, ct);
        var ownName = account == p.DestinationAccountId
            ? await db.Database.SqlQuery<string>(
                $"""SELECT name::text AS "Value" FROM public.billing_accounts WHERE id = {account}""").SingleOrDefaultAsync(ct)
            : null;
        return Project(p, await ConsentsAsync(transfer, ct), account, owner, ownName);
    }

    private static CoverageTransferV1 Project(Proposal p, ConsentRow[] rows, Guid? viewerAccount, LiveOwner? viewer,
        string? destinationName)
    {
        var consents = Purposes.Select(purpose => Status(purpose, rows)).ToArray();
        var collected = p.Blocker is null && consents.All(c => c.Status == Accepted);
        var state = p.Blocker switch
        {
            null => collected ? ConsentsCollected : AwaitingConsents,
            "Expired" => Expired,
            _ => Retired,
        };
        // The destination is named to the other parties only once its own live owner has accepted.
        destinationName ??= Current(DestinationBillingOwner, rows) is { Kind: Accepted, Current: true } accepted
            ? accepted.AccountName : null;
        string[] purposes = viewer is null ? [] : viewerAccount == p.SourceAccountId ? [SourceBillingOwner] : [DestinationBillingOwner];
        return new(p.TransferId, p.ProposalRevision, p.TermsDigest, p.SourceAccountId, p.SourceAccountName,
            p.DestinationAccountId, destinationName, p.ShopId, p.ShopName, p.Treatment, p.CutoverAt, p.SourcePaidThroughAt,
            p.ObservedPermitCount, p.ObservedPermitDeadline, true, p.CreatedAt, p.ExpiresAt, state,
            p.Blocker is null or "Expired" ? null : p.Blocker, consents, collected, collected, purposes, viewer?.Revision);
    }

    /// <summary>The latest decision of a purpose. A revocation always wins its generation, whatever order rows arrived in.</summary>
    private static ConsentRow? Current(string purpose, ConsentRow[] rows) =>
        rows.Where(r => r.Purpose == purpose).OrderByDescending(r => r.Generation)
            .ThenByDescending(r => r.Kind == Revoked).FirstOrDefault();

    private static CoverageTransferConsentStatusV1 Status(string purpose, ConsentRow[] rows) => Current(purpose, rows) switch
    {
        null => new(purpose, AwaitingConsent, 0, null),
        { Kind: Revoked } r => new(purpose, Revoked, r.Generation, r.RecordedAt),
        // An accepted consent stands only while the exact authority that gave it is still the live owner.
        { } r => new(purpose, r.Current ? Accepted : Stale, r.Generation, r.RecordedAt),
    };

    private Task<Proposal?> ProposalAsync(Guid transfer, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Proposal>($"""
            SELECT p.transfer_id, p.proposal_revision, p.terms_digest, p.source_account_id, p.source_account_name::text AS source_account_name,
                p.destination_account_id, p.source_term_id, p.shop_id, p.shop_name, p.source_affiliation_id, p.treatment,
                p.cutover_at, p.source_paid_through_at, p.observed_permit_count, p.observed_permit_deadline,
                p.created_at, p.expires_at, public.coverage_transfer_blocker(p.transfer_id, {now}) AS blocker
            FROM public.coverage_transfer_proposals p WHERE p.transfer_id = {transfer}
            """).SingleOrDefaultAsync(ct);

    private Task<ConsentRow[]> ConsentsAsync(Guid transfer, CancellationToken ct) =>
        db.Database.SqlQuery<ConsentRow>($"""
            SELECT c.event_id, c.purpose, c.generation, c.kind, c.account_id, c.account_name::text AS account_name,
                c.actor_id, c.member_id, c.authority_revision, c.operation_key, c.request_hash, c.recorded_at,
                CASE WHEN c.purpose = 'ShopOwner' THEN true ELSE public.coverage_transfer_owner_current(
                    c.account_id, c.actor_id, c.member_id, c.authority_revision) END AS current
            FROM public.coverage_transfer_consents c WHERE c.transfer_id = {transfer}
            """).ToArrayAsync(ct);

    private sealed record Proposal(Guid TransferId, long ProposalRevision, string TermsDigest, Guid SourceAccountId,
        string SourceAccountName, Guid DestinationAccountId, Guid SourceTermId, Guid ShopId, string ShopName,
        Guid SourceAffiliationId, string Treatment, DateTimeOffset? CutoverAt, DateTimeOffset SourcePaidThroughAt,
        int ObservedPermitCount, DateTimeOffset? ObservedPermitDeadline, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
        string? Blocker);

    private sealed record ConsentRow(Guid EventId, string Purpose, int Generation, string Kind, Guid AccountId,
        string? AccountName, Guid ActorId, Guid MemberId, long AuthorityRevision, Guid? OperationKey, string? RequestHash,
        DateTimeOffset RecordedAt, bool Current);
}
