using ErrorOr;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageTransferStore : ICoverageTransferApprovalStore
{
    public Task<TransferSummary[]> ListAdminAsync(Guid source, CancellationToken ct) =>
        db.Database.SqlQuery<TransferSummary>($"""
            SELECT transfer_id, shop_id, shop_name, destination_account_id FROM public.coverage_transfer_proposals
            WHERE source_account_id = {source} ORDER BY created_at DESC, transfer_id LIMIT {CoverageTransfersV1.PageSize}
            """).ToArrayAsync(ct);

    public async Task<ErrorOr<TransferReview>> ReviewAsync(Guid source, Guid transfer, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        if (await ProposalAsync(transfer, now, ct) is not { } p || p.SourceAccountId != source)
            return CoverageTransferErrors.NotFound;
        var rows = await ConsentsAsync(transfer, ct);
        var consents = Purposes.Select(purpose => Status(purpose, rows)).ToArray();
        var live = await terms.SourceAsync(source, p.SourceTermId, now, ct);
        var cutover = p.CutoverAt ?? p.SourcePaidThroughAt;
        var blocker = p.Blocker ?? (live is not { Basis: "Paid", Suspended: false, Closed: false, Superseded: false }
            ? "SourceChanged" : p.ObservedPermitDeadline > cutover ? "OutstandingPermit"
            : consents.FirstOrDefault(c => c.Status != CoverageTransfersV1.Accepted)?.Status);
        var sourceTerms = await SchedulesAsync(source, transfer, ct);
        await SetAccountAsync(p.DestinationAccountId, ct);
        var destinationTerms = await SchedulesAsync(p.DestinationAccountId, transfer, ct);
        await SetAccountAsync(source, ct);
        var digest = CheckoutOrdersV1.Digest("coverage-transfer-review/1", p.TermsDigest, live?.MemberAllowance,
            Purposes.Select(purpose => Current(purpose, rows)?.EventId).ToArray(), sourceTerms, destinationTerms);
        var key = await db.Database.SqlQuery<Guid?>($"""
            SELECT operation_key AS "Value" FROM public.coverage_transfer_approvals WHERE transfer_id={transfer}
            """).SingleOrDefaultAsync(ct);
        var approval = key is { } operation ? await LookupAsync(source, operation, ct) : null;
        return new TransferReview(transfer, p.ProposalRevision, p.TermsDigest, source, p.SourceAccountName,
            p.DestinationAccountId, Current(CoverageTransfersV1.DestinationBillingOwner, rows)?.AccountName,
            p.ShopId, p.ShopName, p.SourceAffiliationId, p.SourceTermId, p.Treatment, cutover, p.SourcePaidThroughAt,
            p.ExpiresAt, live?.MemberAllowance, sourceTerms, destinationTerms, consents, p.ObservedPermitCount,
            p.ObservedPermitDeadline, true, blocker, digest, approval?.Approval.State);
    }

    private Task<int> SetAccountAsync(Guid account, CancellationToken ct) => db.Database.ExecuteSqlAsync(
        $"SELECT set_config('app.billing_account_id', {account.ToString("D")}, true)", ct);

    private Task<TransferSchedule[]> SchedulesAsync(Guid account, Guid transfer, CancellationToken ct) =>
        db.Database.SqlQuery<TransferSchedule>($"""
            SELECT term_id, starts_at, paid_through_at, access_ends_at FROM public.coverage_transfer_evidence
            WHERE account_id = {account} AND transfer_id = {transfer} ORDER BY term_id
            """).ToArrayAsync(ct);
}
