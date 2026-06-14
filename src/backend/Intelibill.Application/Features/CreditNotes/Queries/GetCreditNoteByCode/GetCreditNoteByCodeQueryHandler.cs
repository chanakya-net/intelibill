using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;

public sealed class GetCreditNoteByCodeQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ICreditNoteRepository creditNoteRepository,
    ISaleReturnRepository saleReturnRepository,
    ISaleRepository saleRepository)
{
    public async Task<ErrorOr<CreditNoteDto>> HandleAsync(
        GetCreditNoteByCodeQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        var shop = await shopRepository.GetByIdAsync(query.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ActiveShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager or ShopRole.Staff))
            return Errors.CreditNote.UserIsNotOwnerManagerOrStaff;

        var code = CreditNoteCodeNormalizer.Normalize(query.Code);
        var creditNote = await creditNoteRepository.GetByCodeAsync(query.ActiveShopId, code, cancellationToken);
        if (creditNote is null)
            return Errors.CreditNote.CreditNoteNotFound(query.Code);

        var saleReturn = await saleReturnRepository.GetByIdWithItemsAsync(
            query.ActiveShopId,
            creditNote.SaleReturnId,
            cancellationToken);

        var invoiceNumber = string.Empty;
        string? customerName = null;
        var returnNumber = saleReturn?.ReturnNumber ?? string.Empty;

        if (saleReturn is not null)
        {
            var sale = await saleRepository.GetByIdAsync(
                saleReturn.SaleId,
                query.ActiveShopId,
                cancellationToken);

            if (sale is not null)
            {
                invoiceNumber = sale.InvoiceNumber;
                customerName = sale.CustomerName;
            }
        }

        return new CreditNoteDto(
            creditNote.Id,
            creditNote.Code,
            creditNote.Status,
            creditNote.OriginalAmount,
            creditNote.AvailableBalance,
            creditNote.ExpiresAt,
            creditNote.IsVoided,
            creditNote.SaleReturnId,
            creditNote.Reason,
            membership.Role is ShopRole.Owner or ShopRole.Manager ? creditNote.VoidReason : null,
            returnNumber,
            invoiceNumber,
            customerName);
    }
}
