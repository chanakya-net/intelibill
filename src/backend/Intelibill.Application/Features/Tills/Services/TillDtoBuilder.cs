using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Tills.Services;

public sealed class TillDtoBuilder(ITillRepository tills)
{
    public async Task<TillSessionDto> BuildAsync(TillSession session, CancellationToken ct = default)
    {
        var close = await tills.GetCloseAsync(session.ShopId, session.Id, ct);
        var review = close is null ? null : await tills.GetReviewAsync(session.ShopId, close.Id, ct);
        var totals = close?.Totals ?? await tills.GetTotalsAsync(session.ShopId, session.Id, ct);
        var late = await tills.GetLatePostingsAsync(session.ShopId, session.Id, ct);
        return new(session.Id, session.TillId, session.CashierId, session.TillName, session.CashierName,
            session.BusinessDate, session.BusinessTimeZoneId, session.OpenedAt, session.State, session.Version,
            session.OpeningFloat, close?.ExpectedCash ?? session.OpeningFloat + totals.RecordedTenderTotals.GetValueOrDefault(ReceiptTenderMethod.Cash),
            totals.RecordedTenderTotals, totals.StoreCreditIssued, totals.StoreCreditRedeemed, totals.ReceivableChange, session.LastMovementSequence,
            close is null ? null : new(close.Id, close.OpeningFloat, close.LastMovementSequence, close.Totals, close.ExpectedCash,
                close.CountedCash, close.Variance, close.ClosedBy, close.ClosedAt, close.Reason, close.OverrideReason, close.IsOverdue,
                close.AwaitingReview, close.BusinessDate, close.TillName, close.CashierName, close.CashierId),
            review is null ? null : new(review.Id, review.CloseId, review.ReviewedBy, review.ReviewedAt, review.Reason),
            late.Select(x => new TillLatePostingDto(x.Id, x.OfflineSubmissionId, x.SaleId, x.CaseId, x.SessionId, x.CloseId,
                x.SourceBusinessDate, x.OccurredAt, x.RecordedAt, x.CashierId, x.TenderLines, x.EvidenceStatus, x.ManagerReason, x.IncludedInCount, x.EvidenceReason)).ToArray());
    }

    public async Task<TillSessionDetailDto> DetailAsync(TillSession session, int page, int pageSize, CancellationToken ct)
    {
        var movements = await tills.GetMovementsAsync(session.ShopId, session.Id, page, pageSize, ct);
        var sources = await tills.GetSourcesAsync(session.ShopId, session.Id, page, pageSize, ct);
        return new(await BuildAsync(session, ct),
            new(movements.Items.Select(x => new TillMovementDto(x.Id, x.Sequence, x.SourceType, x.SourceId, x.Purpose, x.LineNumber,
                x.Method, x.SignedAmount, x.CashTendered, x.Change, x.ExternalReference, x.BankAccountId, x.OccurredAt, x.RecordedAt,
                x.ActorUserId, x.OperationKey, x.Reason, x.OverrideReason, x.ReversesMovementId)).ToArray(), page, pageSize, movements.TotalCount),
            new(sources.Items.Select(x => new TillSourceRecordDto(x.Id, x.SourceType, x.SourceId, x.Purpose, x.OccurredAt,
                x.RecordedAt, x.ActorUserId, x.Settlement, x.OperationKey, x.Reason, x.OverrideReason)).ToArray(), page, pageSize, sources.TotalCount));
    }
}
