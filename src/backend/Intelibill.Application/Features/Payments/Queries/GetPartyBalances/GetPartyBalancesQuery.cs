using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Payments.Queries.GetPartyBalances;

public sealed record GetPartyBalancesQuery(Guid ActorUserId, Guid ActiveShopId, PaymentPartyType PartyType, Guid PartyId);
