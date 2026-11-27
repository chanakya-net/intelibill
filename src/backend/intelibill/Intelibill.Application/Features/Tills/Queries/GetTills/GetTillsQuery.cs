using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Tills.Queries.GetTills;

public sealed record GetTillsQuery(Guid ShopId, Guid ActorUserId);
