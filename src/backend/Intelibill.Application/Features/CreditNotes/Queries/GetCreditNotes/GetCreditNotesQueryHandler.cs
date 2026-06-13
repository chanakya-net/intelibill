using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotes;

public sealed class GetCreditNotesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ICreditNoteRepository creditNoteRepository)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<PaginatedList<CreditNoteListItemDto>>> Handle(
        GetCreditNotesQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        var (rows, totalCount) = await creditNoteRepository.GetPagedAsync(
            query.ShopId,
            query.SearchTerm,
            query.Status,
            pageNumber,
            pageSize,
            cancellationToken);

        var dtos = rows.Select(r => new CreditNoteListItemDto(
            r.CreditNoteId,
            r.Code,
            ComputeStatus(r),
            r.OriginalAmount,
            r.AvailableBalance,
            r.ExpiresAt,
            r.CreatedAt,
            r.SaleReturnId,
            r.ReturnNumber,
            r.SaleId,
            r.InvoiceNumber,
            r.CustomerName)).ToList();

        return new PaginatedList<CreditNoteListItemDto>(dtos, totalCount, pageNumber, pageSize);
    }

    private static CreditNoteStatus ComputeStatus(CreditNoteListRow row)
    {
        if (row.IsVoided)
            return CreditNoteStatus.Voided;

        if (row.AvailableBalance == 0m)
            return CreditNoteStatus.FullyRedeemed;

        if (row.ExpiresAt.HasValue && row.ExpiresAt.Value < DateTimeOffset.UtcNow)
            return CreditNoteStatus.Expired;

        return CreditNoteStatus.Active;
    }
}
