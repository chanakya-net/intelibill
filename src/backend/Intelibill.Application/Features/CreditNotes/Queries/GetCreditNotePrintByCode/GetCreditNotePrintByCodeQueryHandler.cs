using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotePrintByCode;

public sealed class GetCreditNotePrintByCodeQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ICreditNoteRepository creditNoteRepository,
    ISaleReturnRepository saleReturnRepository,
    ISaleRepository saleRepository)
{
    public async Task<ErrorOr<CreditNotePrintDto>> HandleAsync(
        GetCreditNotePrintByCodeQuery query,
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
        var creditNote = await creditNoteRepository.GetByCodeAsync(
            query.ActiveShopId,
            code,
            cancellationToken);
        if (creditNote is null)
            return Errors.CreditNote.CreditNoteNotFound(query.Code);

        var saleReturn = await saleReturnRepository.GetByIdAsync(query.ActiveShopId, creditNote.SaleReturnId, cancellationToken);
        if (saleReturn is null)
            return Errors.CreditNote.CreditNoteNotFound(query.Code);

        var sale = await saleRepository.GetByIdAsync(saleReturn.SaleId, query.ActiveShopId, cancellationToken);
        if (sale is null)
            return Errors.CreditNote.CreditNoteNotFound(query.Code);

        return new CreditNotePrintDto(
            creditNote.Id,
            creditNote.Code,
            creditNote.Status,
            creditNote.Status is CreditNoteStatus.Active,
            creditNote.OriginalAmount,
            creditNote.AvailableBalance,
            creditNote.CreatedAt,
            creditNote.ExpiresAt,
            sale.Id,
            sale.InvoiceNumber,
            saleReturn.Id,
            saleReturn.ReturnNumber,
            string.IsNullOrWhiteSpace(sale.CustomerName) ? "Walk-in Customer" : sale.CustomerName,
            creditNote.Reason,
            membership.Role is ShopRole.Owner or ShopRole.Manager ? creditNote.VoidReason : null);
    }
}
