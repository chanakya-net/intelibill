using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;

public sealed class DeclareShopTaxProfileCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IBankAccountRepository bankAccountRepository,
    ITenantWriteScope tenantWriteScope,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(DeclareShopTaxProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var declared = Declare(shop, command.Profile, command.GstNumber, command.UserId, timeProvider.GetUtcNow());
        if (declared.IsError)
            return declared.Errors;

        shopRepository.Update(shop);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // bank_accounts is tenant data of a shop that need not be the caller's active one; the
        // owner membership re-read above authorizes the read, which is rolled back (see UpdateShop).
        BankAccount? primaryAccount;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await tenantWriteScope.EnterAsync(command.ShopId, cancellationToken);

            var bankAccounts = await bankAccountRepository.FindAsync(x => x.ShopId == command.ShopId, cancellationToken);
            primaryAccount = bankAccounts.Count > 0 ? bankAccounts[0] : null;
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }

        return new ShopDetailsDto(
            shop.Id,
            shop.Name,
            shop.Address,
            shop.City,
            shop.State,
            shop.Pincode,
            shop.ContactPerson,
            shop.MobileNumber,
            shop.GstNumber,
            primaryAccount?.BankName,
            primaryAccount?.AccountNumber,
            primaryAccount?.AccountType?.ToString(),
            primaryAccount?.IfscCode,
            primaryAccount?.AccountHolderName)
        {
            TaxProfile = ShopTaxProfileDto.From(shop),
        };
    }

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
