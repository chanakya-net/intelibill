using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Queries.GetTillSession;

public sealed class GetTillSessionQueryHandler(TillReadService service)
{
    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<TillSessionDetailDto>> HandleAsync(GetTillSessionQuery query, CancellationToken ct) => service.SessionAsync(query.ShopId, query.ActorUserId, query.SessionId, query.Page, query.PageSize, ct);
}
