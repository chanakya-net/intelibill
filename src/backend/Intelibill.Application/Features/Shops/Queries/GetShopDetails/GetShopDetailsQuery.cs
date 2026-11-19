using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.Shops.Queries.GetShopDetails;

public sealed record GetShopDetailsQuery(Guid UserId, Guid ShopId)
{
    public SessionClaims? Session { get; init; }
}
