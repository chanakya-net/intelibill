using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Queries.GetTillCapabilities;

public sealed class GetTillCapabilitiesQueryHandler(TillReadService service)
{
    public Task<ErrorOr<TillCapabilitiesDto>> HandleAsync(GetTillCapabilitiesQuery query, CancellationToken ct) => service.CapabilitiesAsync(query.ShopId, query.ActorUserId, ct);
}
