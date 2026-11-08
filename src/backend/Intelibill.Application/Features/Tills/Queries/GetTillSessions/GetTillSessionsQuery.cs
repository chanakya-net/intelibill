using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Tills.Queries.GetTillSessions;

public sealed record GetTillSessionsQuery(Guid ShopId, Guid ActorUserId, TillSessionState? State = null, DateOnly? BusinessDate = null, int Page = 1, int PageSize = 20);
