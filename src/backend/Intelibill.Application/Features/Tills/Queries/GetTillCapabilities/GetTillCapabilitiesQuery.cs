using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Tills.Queries.GetTillCapabilities;

public sealed record GetTillCapabilitiesQuery(Guid ShopId, Guid ActorUserId);
