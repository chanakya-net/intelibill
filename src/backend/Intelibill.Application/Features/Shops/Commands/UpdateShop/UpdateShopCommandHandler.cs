using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShop;

public sealed class UpdateShopCommandHandler(ShopSetupAccess access, IBankAccountRepository banks)
{
    [LicenceAction(LicenceAction.BasicShopSetup)]
    public Task<ErrorOr<ShopDetailsDto>> HandleAsync(UpdateShopCommand command, CancellationToken ct) =>
        access.WriteAsync<ShopDetailsDto>(command.Session, command.UserId, command.ShopId, LicenceAction.BasicShopSetup,
            async (shop, lease, token) =>
            {
                var accounts = await banks.FindAsync(x => x.ShopId == shop.Id, token);
                var decision = lease.CurrentDecision();
                if (ShopSetupAccess.Denial(decision, allowSetup: true, allowRecovery: true) is { } denied)
                    return denied;

                var before = ProtectedIdentity(shop);
                var updated = shop.UpdateDetails(command.Name, command.Address, command.City, command.State,
                    command.Pincode, command.ContactPerson, command.MobileNumber, command.GstNumber);
                if (ShopSetupAccess.Denial(decision, allowSetup: true) is { } restricted &&
                    (updated.IsError || before != ProtectedIdentity(shop)))
                    return restricted;
                if (updated.IsError) return updated.Errors;

                return ShopDetailsDto.From(shop, accounts.Count > 0 ? accounts[0] : null, ShopSetupAccess.Hint(shop.Id, ShopRole.Owner, decision));
            }, ct);

    private static (string Address, string City, string State, string Pincode, string? GstNumber) ProtectedIdentity(Shop shop) =>
        (shop.Address, shop.City, shop.State, shop.Pincode, shop.GstNumber);
}
