using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Queries.GetShopDetails;

public sealed class GetShopDetailsQueryHandler(ShopSetupAccess access, IShopRepository shops, IBankAccountRepository banks)
{
    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<ShopDetailsDto>> HandleAsync(GetShopDetailsQuery query, CancellationToken ct) =>
        access.ReadAsync<ShopDetailsDto>(query.UserId, query.ShopId, async (hint, token) =>
        {
            var shop = await shops.GetByIdAsync(query.ShopId, token);
            if (shop is null) return Errors.Shop.ShopNotFound;
            var accounts = await banks.FindAsync(x => x.ShopId == query.ShopId, token);
            return ShopDetailsDto.From(shop, accounts.Count > 0 ? accounts[0] : null, hint);
        }, ct);
}
