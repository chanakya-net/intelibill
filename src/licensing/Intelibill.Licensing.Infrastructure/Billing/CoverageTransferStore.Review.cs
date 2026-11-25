using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Shared.Contracts.V1.CoverageTransfersV1;
using Outcome = Intelibill.Licensing.Application.Coverage.CoverageTransferReviewOutcome;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageTransferStore
{
    public async Task<CoverageTransferReviewCheck> CaptureAsync(Guid transferId, long expectedRevision, string termsDigest,
        CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var check = await EvaluateInTransactionAsync(transferId, expectedRevision, termsDigest, ct);
            await tx.CommitAsync(ct);
            return check;
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return CoverageTransferReviewCheck.Of(Outcome.Unavailable, "StorageUnavailable");
        }
    }

    public async Task<CoverageTransferReviewCheck> EvaluateInTransactionAsync(Guid transferId, long expectedRevision,
        string termsDigest, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('app.coverage_transfer_id', {transferId.ToString("D")}, true)", ct);
        var parties = await db.Database.SqlQuery<Parties>($"""
            SELECT source_account_id, destination_account_id FROM public.coverage_transfer_proposals
            WHERE transfer_id = {transferId}
            """).SingleOrDefaultAsync(ct);
        if (parties is null) return CoverageTransferReviewCheck.Of(Outcome.Stale, "NotFound");
        // Canonical pair order before any proposal or membership fact is trusted; facts are re-read after the waits.
        foreach (var account in new[] { parties.SourceAccountId, parties.DestinationAccountId }
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal))
            await BillingAccountLock.TakeAsync(db, account, ct);

        var now = EntitlementSnapshotsV1.Instant(clock);
        if (await ProposalAsync(transferId, now, ct) is not { } p || p.ProposalRevision != expectedRevision ||
            p.TermsDigest != termsDigest)
            return CoverageTransferReviewCheck.Of(Outcome.Stale, "ProposalChanged");
        if (p.Blocker is { } blocker)
            return CoverageTransferReviewCheck.Of(blocker == "Expired" ? Outcome.Expired : Outcome.Stale, blocker);

        var rows = await ConsentsAsync(transferId, ct);
        var bindings = new List<CoverageTransferConsentBinding>(Purposes.Length);
        foreach (var purpose in Purposes)
        {
            var status = Status(purpose, rows).Status;
            if (status == Stale) return CoverageTransferReviewCheck.Of(Outcome.Stale, purpose + "AuthorityChanged");
            if (status != Accepted) return CoverageTransferReviewCheck.Of(Outcome.Incomplete, purpose + status);
            var row = Current(purpose, rows)!;
            bindings.Add(new(purpose, row.EventId, row.AccountId, row.ActorId, row.MemberId, row.AuthorityRevision, row.Generation));
        }
        return new(Outcome.Current, null, new(p.TransferId, p.ProposalRevision, p.TermsDigest, p.SourceAccountId,
            p.DestinationAccountId, p.ShopId, p.SourceAffiliationId, p.ObservedPermitCount, p.ObservedPermitDeadline,
            [.. bindings], now));
    }

    private sealed record Parties(Guid SourceAccountId, Guid DestinationAccountId);
}
