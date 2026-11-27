using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Queries.GetTillOperation;

public sealed class GetTillOperationQueryHandler(TillReadService service)
{
    [LicenceAction(LicenceAction.Recovery)]
    public Task<ErrorOr<TillOperationDto>> HandleAsync(GetTillOperationQuery query, CancellationToken ct) => service.OperationAsync(query.ShopId, query.ActorUserId, query.OperationType, query.OperationKey, ct);
}
