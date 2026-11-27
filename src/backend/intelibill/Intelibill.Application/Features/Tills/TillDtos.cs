using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills;

public sealed record TillDto(Guid Id, string Name);
public sealed record TillSessionDto(Guid Id, Guid TillId, Guid CashierId, string TillName, string CashierName,
    DateOnly BusinessDate, string BusinessTimeZoneId, DateTimeOffset OpenedAt, TillSessionState State, uint Version,
    decimal OpeningFloat, decimal ExpectedCash, IReadOnlyDictionary<ReceiptTenderMethod, decimal> RecordedTenderTotals,
    decimal StoreCreditIssued, decimal StoreCreditRedeemed, decimal ReceivableChange, long LastMovementSequence,
    TillCloseDto? Close, TillCloseReviewDto? Review, IReadOnlyList<TillLatePostingDto> LatePostings)
{
    public bool AwaitingReview => Close?.AwaitingReview == true && Review is null;
}
public sealed record TillCloseDto(Guid Id, decimal OpeningFloat, long LastMovementSequence, TillTotals Totals,
    decimal ExpectedCash, decimal CountedCash, decimal Variance, Guid ClosedBy, DateTimeOffset ClosedAt,
    string? Reason, string? OverrideReason, bool IsOverdue, bool AwaitingReview, DateOnly BusinessDate, string TillName, string CashierName, Guid CashierId);
public sealed record TillCloseReviewDto(Guid Id, Guid CloseId, Guid ReviewedBy, DateTimeOffset ReviewedAt, string Reason);
public sealed record TillMovementDto(Guid Id, long Sequence, TillMovementSource SourceType, Guid SourceId, string Purpose,
    int LineNumber, ReceiptTenderMethod Method, decimal SignedAmount, decimal? CashTendered, decimal? Change,
    string? ExternalReference, Guid? BankAccountId, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, Guid ActorUserId,
    string OperationKey, string? Reason, string? OverrideReason, Guid? ReversesMovementId, Guid? ParentSaleId = null, Guid? PaymentReceiptId = null, Guid? ExpenseId = null);
public sealed record TillSourceRecordDto(Guid Id, TillMovementSource SourceType, Guid SourceId, string Purpose,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, Guid ActorUserId, TillNonCashSettlement Settlement, string OperationKey, string? Reason, string? OverrideReason, Guid? ParentSaleId = null, Guid? PaymentReceiptId = null, Guid? ExpenseId = null);
public sealed record TillLatePostingDto(Guid Id, Guid OfflineSubmissionId, Guid SaleId, Guid CaseId, Guid? SessionId,
    Guid? CloseId, DateOnly SourceBusinessDate, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, Guid CashierId,
    IReadOnlyList<TenderLine> TenderLines, string EvidenceStatus, string ManagerReason, bool IncludedInCount, string? EvidenceReason = null)
{
    public decimal? SupplementalAmount => EvidenceStatus == "Recorded" ? TenderLines.Sum(x => x.AppliedAmount) : null;
}
public sealed record TillPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record TillSessionDetailDto(TillSessionDto Session, TillPage<TillMovementDto> Movements,
    TillPage<TillSourceRecordDto> Sources);

public sealed record TillCapabilitiesDto(bool Enabled, bool MixedTenderEnabled, DateOnly BusinessDate);

public sealed record TillOperationDto(string OperationType, string OperationKey, Guid TargetId, Guid ActorUserId, DateTimeOffset CompletedAt, object Response);
