using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Shared.Contracts.V1.CoverageTransfersV1;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageTransferStore
{
    public async Task<ErrorOr<CoverageTransferV1>> DecideAsync(Guid account, Guid actor, Guid transfer, string purpose,
        string kind, CoverageTransferDecisionV1 d, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        if (await ProposalAsync(transfer, now, ct) is not { } p) return CoverageTransferErrors.NotFound;
        if (account != (purpose == SourceBillingOwner ? p.SourceAccountId : p.DestinationAccountId) ||
            await OwnerAsync(account, actor, ct) is not { } owner)
            return CoverageTransferErrors.PurposeDenied;

        var hash = DecisionHash(transfer, purpose, kind, d);
        if (await LookupDecisionAsync(account, actor, d.OperationKey, ct) is { } prior)
            return Replay(prior, hash, owner) ?? await ViewAsync(transfer, account, actor, now, ct);

        if (p.ApprovalState is "Consuming" or "PendingActivation" or "Applied" or "NeedsReview") return CoverageTransferErrors.Changed;
        if (d.ExpectedProposalRevision != p.ProposalRevision || d.ExpectedTermsDigest != p.TermsDigest ||
            d.ExpectedAuthorityRevision != owner.Revision)
            return CoverageTransferErrors.Changed;
        var status = Status(purpose, await ConsentsAsync(transfer, ct));
        var generation = Generation(kind, status, p.Blocker);
        if (generation.IsError) return generation.Errors;

        var name = await db.Database.SqlQuery<string>(
            $"""SELECT name::text AS "Value" FROM public.billing_accounts WHERE id = {account}""").SingleAsync(ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_consents (event_id, transfer_id, source_account_id, destination_account_id,
                purpose, generation, kind, account_id, account_name, actor_id, member_id, authority_revision,
                proposal_revision, terms_digest, operation_key, request_hash, recorded_at)
            VALUES ({Guid.NewGuid()}, {transfer}, {p.SourceAccountId}, {p.DestinationAccountId}, {purpose},
                {generation.Value}, {kind}, {account}, {name}, {actor}, {owner.Id}, {owner.Revision},
                {p.ProposalRevision}, {p.TermsDigest}, {d.OperationKey}, {hash}, {now})
            ON CONFLICT DO NOTHING
            """, ct);
        return inserted == 1 ? await ViewAsync(transfer, account, actor, now, ct) : CoverageTransferErrors.ConsentInvalid;
    }

    public async Task<ErrorOr<CoverageTransferV1>> RecoverDecisionAsync(Guid account, Guid actor, Guid transfer,
        string purpose, Guid key, CancellationToken ct)
    {
        if (await LookupDecisionAsync(account, actor, key, ct) is not { } prior || prior.TransferId != transfer ||
            prior.Purpose != purpose)
            return CoverageTransferErrors.NotFound;
        return await OwnerAsync(account, actor, ct) is { } owner && owner.Id == prior.MemberId &&
            owner.Revision == prior.AuthorityRevision
            ? await ViewAsync(transfer, account, actor, EntitlementSnapshotsV1.Instant(clock), ct)
            : CoverageTransferErrors.AuthorityChanged;
    }

    /// <summary>
    /// Records the named shop owner's signed merchant decision. Called under the source account lock with the inbox
    /// row already written in this transaction; only evidence is appended. False rejects a payload that does not
    /// match the frozen proposal, which leaves the inbox event unrecorded.
    /// </summary>
    internal async Task<bool> ApplyShopConsentAsync(CoverageTransferShopConsentV1 c, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('app.coverage_transfer_id', {c.TransferId.ToString("D")}, true)", ct);
        return await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_consents (event_id, transfer_id, source_account_id, destination_account_id,
                purpose, generation, kind, account_id, actor_id, member_id, authority_revision, proposal_revision,
                terms_digest, recorded_at)
            SELECT {c.EventId}, p.transfer_id, p.source_account_id, p.destination_account_id, 'ShopOwner', {c.Generation},
                {c.Kind}, p.source_account_id, {c.ActorId}, {c.MembershipId}, {c.AuthorityVersion}, p.proposal_revision,
                p.terms_digest, {c.RecordedAt}
            FROM public.coverage_transfer_proposals p
            WHERE p.transfer_id = {c.TransferId} AND p.source_account_id = {c.AccountId}
                AND p.destination_account_id = {c.DestinationAccountId} AND p.shop_id = {c.ShopId}
                AND p.source_affiliation_id = {c.AffiliationRequestId} AND p.proposal_revision = {c.ProposalRevision}
                AND p.terms_digest = {c.TermsDigest}
            ON CONFLICT (transfer_id, purpose, generation, kind) DO NOTHING
            """, ct) == 1;
    }

    // An identical retry returns the original receipt even after progress advanced, but only to the exact authority
    // that made it: an owner who lost and regained the role holds a new membership revision and must decide afresh.
    private static Error? Replay(PriorDecision prior, string hash, LiveOwner owner) =>
        prior.Hash != hash ? BillingAccountErrors.PayloadMismatch
        : prior.MemberId != owner.Id || prior.AuthorityRevision != owner.Revision ? CoverageTransferErrors.AuthorityChanged
        : null;

    private static ErrorOr<int> Generation(string kind, CoverageTransferConsentStatusV1 status, string? blocker) => kind switch
    {
        Accepted when blocker is not null => CoverageTransferErrors.Changed,
        Accepted when status.Status is AwaitingConsent or Revoked or Stale => status.Generation + 1,
        Revoked when status.Status is Accepted or Stale => status.Generation,
        _ => CoverageTransferErrors.ConsentInvalid,
    };

    private static string DecisionHash(Guid transfer, string purpose, string kind, CoverageTransferDecisionV1 d) =>
        CheckoutOrdersV1.Digest("coverage-transfer-consent/1", transfer, purpose, kind, d.ExpectedProposalRevision,
            d.ExpectedTermsDigest, d.ExpectedAuthorityRevision);

    private Task<PriorDecision?> LookupDecisionAsync(Guid account, Guid actor, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<PriorDecision>($"""
            SELECT transfer_id, purpose, request_hash AS hash, member_id, authority_revision
            FROM public.coverage_transfer_consents
            WHERE account_id = {account} AND actor_id = {actor} AND operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    private sealed record PriorDecision(Guid TransferId, string Purpose, string Hash, Guid MemberId, long AuthorityRevision);
}
