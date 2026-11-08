using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Queries.GetTillOperation;

public sealed class GetTillOperationQueryHandler(TillReadService service)
{
    public Task<ErrorOr<TillOperationDto>> HandleAsync(GetTillOperationQuery query, CancellationToken ct) => service.OperationAsync(query.ShopId, query.ActorUserId, query.OperationType, query.OperationKey, ct);
}
