using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Queries.GetTillSessions;

public sealed class GetTillSessionsQueryHandler(TillReadService service)
{
    public Task<ErrorOr<TillPage<TillSessionDto>>> HandleAsync(GetTillSessionsQuery query, CancellationToken ct) => service.SessionsAsync(query.ShopId, query.ActorUserId, query.State, query.BusinessDate, query.Page, query.PageSize, ct);
}
