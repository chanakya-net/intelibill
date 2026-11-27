using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed partial class OfflineSalePoster
{
    private sealed record TillPreparation(TillSession? NormalSession, TillSession? HistoricalSession, TillClose? Close);

    private async Task<ErrorOr<TillPreparation>> PrepareTillAsync(OfflineSalePostingRequest request, DateOnly businessDate, CancellationToken ct)
    {
        var gate = await tillPosting.GateAsync(request.ShopId, ct);
        if (gate.IsError) return gate.Errors;
        var sale = request.Sale;
        var hasContext = sale.Till is not null || sale.TenderLines is not null || sale.TillBusinessDate is not null || sale.TillCashierId is not null;
        if (request.ReviewedTill is { } reviewed)
        {
            if (!gate.Value) return Errors.Till.Disabled;
            if (request.OfflineSubmissionId is null || reviewed.CaseId == Guid.Empty) return Errors.Till.ReviewRequired;
            var evidence = reviewed.Evidence;
            if (evidence is null || evidence.TenderLines is null || evidence.TenderLines.Any(x => x is null) ||
                string.IsNullOrWhiteSpace(evidence.Reason) || evidence.Reason.Trim().Length > 500 || !StorableText.IsStorable(evidence.Reason))
                return Errors.Till.ReviewRequired;
            if (evidence.TenderLines.Any(line => !StorableText.IsStorable(line.ExternalReference))) return Errors.Sale.OfflineTextInvalid;
            if (evidence.EvidenceStatus == "Unknown")
            {
                // Only a genuinely old envelope may remain unknown. Present new evidence must be validated.
                if (hasContext || evidence.TenderLines.Count != 0) return Errors.Till.ReviewRequired;
            }
            else if (evidence.EvidenceStatus == "Recorded")
            {
                // Only wholly legacy Credit invoices with an external paid amount leave its
                // method ambiguous. Their actual tender is separately reasoned evidence;
                // every unambiguous method and every new-field invoice must conserve it.
                var valid = !hasContext && sale.PaymentMethod == PaymentMethod.Credit && sale.PaidAmount > 0m
                    ? TenderLine.ValidateLines(evidence.TenderLines, sale.PaidAmount)
                    : SaleTenderPolicy.Validate(sale.PaymentMethod, sale.PaidAmount, evidence.TenderLines);
                if (valid.IsError) return valid.Errors;
                if (hasContext)
                {
                    var frozen = SaleTenderPolicy.Validate(sale.PaymentMethod, sale.PaidAmount, sale.TenderLines);
                    if (frozen.IsError) return frozen.Errors;
                    if (sale.TenderLines is null || !sale.TenderLines.SequenceEqual(evidence.TenderLines)) return Errors.Till.ReviewRequired;
                }
                foreach (var line in evidence.TenderLines)
                    if (line.BankAccountId is { } bank && (await banks.GetByIdAsync(bank, ct))?.ShopId != request.ShopId)
                        return Error.Validation("Till.TenderReferenceInvalid", "The bank account must belong to this shop.");
            }
            else return Errors.Till.ReviewRequired;

            TillSession? historical = null;
            TillClose? close = null;
            if (sale.Till is { } claimed)
            {
                var locked = await tills.LockSessionAsync(request.ShopId, claimed.SessionId, ct);
                if (locked == RowLockOutcome.TimedOut) return Errors.Till.Busy;
                if (locked == RowLockOutcome.Acquired)
                {
                    var candidate = await tills.ReloadSessionAsync(request.ShopId, claimed.SessionId, ct);
                    close = await tills.GetCloseAsync(request.ShopId, claimed.SessionId, ct);
                    if (candidate is not null && MatchesHistorical(request, candidate, businessDate) &&
                        (close is null || sale.SoldAt <= close.ClosedAt)) historical = candidate;
                }
            }
            // A review is not an alternative way to omit cash that can still enter its original
            // count. The caller can use ordinary replay while this session remains current/open.
            if (OfflineTillReconciliationEligibility.IsCurrentOpen(historical, businessClock, request.Now))
                return Errors.Till.ReviewNotRequired;
            return new TillPreparation(null, historical, historical is null ? null : close);
        }

        if (!gate.Value && !hasContext) return new TillPreparation(null, null, null);
        // Frozen issuer/cashier/date are evidence, not permission to override another drawer.
        if (sale.Till is null || sale.TenderLines is null || sale.IssuedByUserId != request.AttributedUserId ||
            sale.TillCashierId != request.AttributedUserId || sale.TillBusinessDate != businessDate) return Errors.Till.ReviewRequired;
        var tender = SaleTenderPolicy.Validate(sale.PaymentMethod, sale.PaidAmount, sale.TenderLines);
        if (tender.IsError) return tender.Errors;
        var acquisition = await tillPosting.AcquireAsync(request.ShopId, request.AttributedUserId, sale.Till, businessDate, ct);
        if (acquisition.IsError) return acquisition.Errors;
        if (acquisition.Value is not { } session || !MatchesHistorical(request, session, businessDate)) return Errors.Till.ReviewRequired;
        return new TillPreparation(session, null, null);
    }

    private static bool MatchesHistorical(OfflineSalePostingRequest request, TillSession session, DateOnly businessDate) =>
        OfflineTillReconciliationEligibility.MatchesHistorical(request.ShopId, request.Sale, session, request.AttributedUserId, businessDate, request.Now);

    private async Task<ErrorOr<Success>> StageTillAsync(OfflineSalePostingRequest request, Sale sale, TillPreparation till, CancellationToken ct)
    {
        if (request.ReviewedTill is { } reviewed)
        {
            var late = TillLatePosting.Record(request.ShopId, request.OfflineSubmissionId!.Value, sale.Id, reviewed.CaseId,
                till.HistoricalSession?.Id, till.Close?.Id, sale.BusinessDate, sale.SoldAt, request.Now, request.AttributedUserId,
                reviewed.Evidence.TenderLines, reviewed.Evidence.EvidenceStatus, reviewed.ManagerReason);
            if (late.IsError) return late.Errors;
            late.Value.AttachEvidenceReason(reviewed.Evidence.Reason);
            await tills.AddLatePostingAsync(late.Value, ct);
        }
        else if (till.NormalSession is { } session)
        {
            var attached = sale.AttachTillSettlement(session.Id, request.Sale.Till!.OverrideReason, request.Sale.TenderLines ?? []);
            if (attached.IsError) return attached.Errors;
            var appended = await tillPosting.AppendAsync(session, new(TillMovementSource.OfflineSale, sale.Id, "sale",
                request.Sale.TenderLines ?? [], 1m, sale.SoldAt, sale.IdempotencyKey, null,
                ReceivableChange: sale.DueAmount), request.AttributedUserId, ct);
            if (appended.IsError) return appended.Errors;
        }
        return Result.Success;
    }
}
