using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Queries.GetTills;

public sealed class GetTillsQueryHandler(TillReadService service)
{
    public Task<ErrorOr<IReadOnlyList<TillDto>>> HandleAsync(GetTillsQuery query, CancellationToken ct) => service.TillsAsync(query.ShopId, query.ActorUserId, ct);
}
