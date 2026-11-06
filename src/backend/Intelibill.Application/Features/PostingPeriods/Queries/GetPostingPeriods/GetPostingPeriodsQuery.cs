namespace Intelibill.Application.Features.PostingPeriods.Queries.GetPostingPeriods;

/// <param name="FiscalYearStart">The calendar year the fiscal year starts in: 2026 is April 2026 to March 2027. Zero means the current one.</param>
public sealed record GetPostingPeriodsQuery(Guid ShopId, Guid UserId, int FiscalYearStart);
