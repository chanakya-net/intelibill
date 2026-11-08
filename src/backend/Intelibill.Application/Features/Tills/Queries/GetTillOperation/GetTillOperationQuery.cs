using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Tills.Queries.GetTillOperation;

public sealed record GetTillOperationQuery(Guid ShopId, Guid ActorUserId, string OperationType, string OperationKey);
