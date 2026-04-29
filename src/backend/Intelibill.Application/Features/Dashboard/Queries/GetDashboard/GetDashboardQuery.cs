namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed record GetDashboardQuery(Guid UserId, Guid ShopId, DateOnly StartDate, DateOnly EndDate);
