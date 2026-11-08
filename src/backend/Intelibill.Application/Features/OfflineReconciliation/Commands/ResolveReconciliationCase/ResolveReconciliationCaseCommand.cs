namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

/// <param name="OperationId">The idempotency key: a resend with the same key replays the committed answer.</param>
/// <param name="ExpectedVersion">The case version the reviewer was shown; anything newer is a conflict.</param>
/// <param name="Action">A <see cref="Domain.Enums.ReconciliationAction"/> name.</param>
/// <param name="Reason">Why, in the reviewer's words (1–500 characters after trimming).</param>
/// <param name="LinkedOperationId">The inventory adjustment or sale a link action points at.</param>
public sealed record ResolveReconciliationCaseCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid CaseId,
    string OperationId,
    uint ExpectedVersion,
    string Action,
    string Reason,
    Guid? LinkedOperationId,
    Intelibill.Application.Features.Sales.Commands.SyncOfflineSales.TillReconciliationEvidence? TillReconciliationEvidence = null);
