using ErrorOr;
using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Common.Licensing;

public interface IShopTradingAuthorityReader
{
    Task<ErrorOr<ShopTradingAuthorityV1>> ReadAsync(SessionClaims session, Guid shopId, CancellationToken ct);
}

public sealed record ShopTradingAuthorityV1(int SchemaVersion, Guid ShopId, DateTimeOffset AuthorityVerifiedAt,
    DateTimeOffset ValidUntil, bool Enforced);
