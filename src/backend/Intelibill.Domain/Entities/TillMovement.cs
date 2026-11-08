using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class TillMovement : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid SourceRecordId { get; private set; }
    public long Sequence { get; private set; }
    public TillMovementSource SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public int LineNumber { get; private set; }
    public ReceiptTenderMethod Method { get; private set; }
    public decimal SignedAmount { get; private set; }
    public decimal? CashTendered { get; private set; }
    public decimal? Change { get; private set; }
    public string? ExternalReference { get; private set; }
    public Guid? BankAccountId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? OverrideReason { get; private set; }
    public Guid? ReversesMovementId { get; private set; }
    private TillMovement() { }
    public static ErrorOr<TillMovement> Record(TillSession session, TillSourceRecord source, int lineNumber, TenderLine tender,
        decimal direction, Guid actorId, DateTimeOffset recordedAt, Guid? reversesMovementId)
    {
        var valid = tender.Validate();
        if (valid.IsError) return valid.Errors;
        if (source.ShopId != session.ShopId || source.SessionId != session.Id || lineNumber != 1 || direction is not (1m or -1m) ||
            actorId == Guid.Empty || actorId != source.ActorUserId || reversesMovementId == Guid.Empty || recordedAt < source.OccurredAt)
            return Error.Validation("Till.MovementInvalid", "A movement must belong to this session and have one signed external tender.");
        var sequence = session.NextSequence();
        if (sequence.IsError) return sequence.Errors;
        return new TillMovement { ShopId = session.ShopId, SessionId = session.Id, SourceRecordId = source.Id, Sequence = sequence.Value,
            SourceType = source.SourceType, SourceId = source.SourceId, Purpose = source.Purpose, LineNumber = lineNumber,
            Method = tender.Method, SignedAmount = tender.AppliedAmount * direction, CashTendered = tender.CashTendered, Change = tender.Change,
            ExternalReference = tender.ExternalReference, BankAccountId = tender.BankAccountId, OccurredAt = source.OccurredAt, RecordedAt = recordedAt,
            ActorUserId = actorId, OperationKey = source.OperationKey, Reason = source.Reason, OverrideReason = source.OverrideReason,
            ReversesMovementId = reversesMovementId };
    }
}
