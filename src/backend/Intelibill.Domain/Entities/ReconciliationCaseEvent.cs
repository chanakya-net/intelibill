using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>An immutable audit entry: one action taken on a reconciliation case and the status change it made.</summary>
public sealed class ReconciliationCaseEvent : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid CaseId { get; private set; }
    public ReconciliationAction Action { get; private set; }
    public ReconciliationCaseStatus FromStatus { get; private set; }
    public ReconciliationCaseStatus ToStatus { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid? LinkedOperationId { get; private set; }
    public string? OperationKey { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private ReconciliationCaseEvent() { }

    public static ReconciliationCaseEvent Record(
        Guid shopId,
        Guid caseId,
        ReconciliationAction action,
        ReconciliationCaseStatus fromStatus,
        ReconciliationCaseStatus toStatus,
        Guid actorUserId,
        string reason,
        Guid? linkedOperationId,
        string? operationKey,
        DateTimeOffset occurredAt)
    {
        return new ReconciliationCaseEvent
        {
            ShopId = shopId,
            CaseId = caseId,
            Action = action,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorUserId = actorUserId,
            Reason = reason.Trim(),
            LinkedOperationId = linkedOperationId,
            OperationKey = operationKey,
            OccurredAt = occurredAt,
        };
    }
}
