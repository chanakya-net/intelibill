namespace Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCases;

/// <param name="Status">open (open and escalated), escalated, resolved or all; blank means open.</param>
/// <param name="Page">1-based; values below 1 read the first page.</param>
/// <param name="PageSize">Clamped to 1–100.</param>
public sealed record GetReconciliationCasesQuery(
    Guid ActorUserId,
    Guid ShopId,
    string? Status,
    int Page = 1,
    int PageSize = 20);
