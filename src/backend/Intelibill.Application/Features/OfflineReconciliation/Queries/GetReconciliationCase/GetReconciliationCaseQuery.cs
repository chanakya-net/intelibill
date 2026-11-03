namespace Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCase;

public sealed record GetReconciliationCaseQuery(Guid ActorUserId, Guid ShopId, Guid CaseId);
