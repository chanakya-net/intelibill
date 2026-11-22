using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;

public sealed class DeclareShopTaxProfileCommandHandler(ShopSetupAccess access, IBankAccountRepository banks, TimeProvider clock)
{
    [LicenceAction(LicenceAction.BasicShopSetup)]
    public Task<ErrorOr<ShopDetailsDto>> HandleAsync(DeclareShopTaxProfileCommand command, CancellationToken ct) =>
        access.WriteAsync<ShopDetailsDto>(command.Session, command.UserId, command.ShopId, LicenceAction.BasicShopSetup,
            async (shop, lease, token) =>
            {
                var accounts = await banks.FindAsync(x => x.ShopId == shop.Id, token);
                var decision = lease.CurrentDecision();
                if (ShopSetupAccess.Denial(decision, allowSetup: true) is { } denied) return denied;

                var declared = Declare(shop, command.Profile, command.GstNumber, command.UserId, clock.GetUtcNow());
                if (declared.IsError) return declared.Errors;
                return ShopDetailsDto.From(shop, accounts.Count > 0 ? accounts[0] : null, ShopSetupAccess.Hint(shop.Id, ShopRole.Owner, decision));
            }, ct);

    internal static ErrorOr<Success> Declare(Shop shop, ShopTaxProfileInput profile, string? gstNumber, Guid actorUserId, DateTimeOffset now) =>
        shop.DeclareTaxProfile(
            profile.LegalName,
            profile.StateCode,
            profile.TaxRegime,
            gstNumber,
            profile.InvoiceSeriesCode,
            profile.EInvoiceStatus ?? EInvoiceStatus.NotAttested,
            actorUserId,
            now);
}
