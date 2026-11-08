using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Tills.Queries.GetTillSession;

public sealed record GetTillSessionQuery(Guid ShopId, Guid ActorUserId, Guid SessionId, int Page = 1, int PageSize = 20);
