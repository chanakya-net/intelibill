using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

/// <summary>Unique source attribution, including zero-tender sources and signed non-drawer settlements.</summary>
public sealed class TillSourceRecord : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SessionId { get; private set; }
    public TillMovementSource SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string OperationKey { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? Reason { get; private set; }
    public string? OverrideReason { get; private set; }
    public TillNonCashSettlement Settlement { get; private set; } = new();
    private TillSourceRecord() { }
    public static ErrorOr<TillSourceRecord> Record(TillSession session, TillMovementSource sourceType, Guid sourceId,
        string purpose, string operationKey, Guid actorId, DateTimeOffset occurredAt, DateTimeOffset recordedAt,
        string? reason, string? overrideReason, TillNonCashSettlement settlement)
    {
        if (!Enum.IsDefined(sourceType) || sourceId == Guid.Empty || actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(purpose) || purpose.Length > 100 || string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 128 ||
            reason?.Length > 500 || overrideReason?.Length > 500 || occurredAt < session.OpenedAt || recordedAt < occurredAt ||
            !TillMoney.IsValid(settlement.StoreCreditIssued) || !TillMoney.IsValid(settlement.StoreCreditRedeemed) || !TillMoney.IsValid(settlement.ReceivableChange))
            return Error.Validation("Till.SourceInvalid", "A valid source, operation and signed settlement amounts are required.");
        if (actorId != session.CashierId && string.IsNullOrWhiteSpace(overrideReason))
            return Error.Validation("Till.OverrideReasonRequired", "Posting for another cashier requires a persisted reason.");
        if (sourceType == TillMovementSource.ManualCash && string.IsNullOrWhiteSpace(reason))
            return Error.Validation("Till.ReasonRequired", "Manual cash movements require a reason.");
        var registered = session.RegisterSource();
        if (registered.IsError) return registered.Errors;
        return new TillSourceRecord { ShopId = session.ShopId, SessionId = session.Id, SourceType = sourceType, SourceId = sourceId,
            Purpose = purpose.Trim(), OperationKey = operationKey, ActorUserId = actorId, OccurredAt = occurredAt, RecordedAt = recordedAt,
            Reason = reason?.Trim(), OverrideReason = overrideReason?.Trim(), Settlement = settlement };
    }
}
