using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Queries.GetShopDetails;

public sealed class GetShopDetailsQueryHandler(ShopSetupAccess access, IShopRepository shops, IBankAccountRepository banks)
{
    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<ShopDetailsDto>> HandleAsync(GetShopDetailsQuery query, CancellationToken ct) =>
        access.ReadAsync<Facts, ShopDetailsDto>(query.Session, query.UserId, query.ShopId, async (_, token) =>
        {
            var shop = await shops.GetByIdAsync(query.ShopId, token);
            if (shop is null) return Errors.Shop.ShopNotFound;
            var accounts = await banks.FindAsync(x => x.ShopId == query.ShopId, token);
            return new Facts(shop, accounts.Count > 0 ? accounts[0] : null);
        }, (facts, hint) => ShopDetailsDto.From(facts.Shop, facts.Account, hint), ct);

    private sealed record Facts(Shop Shop, BankAccount? Account);
}
