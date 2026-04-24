using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSales;

public sealed class GetSalesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository)
{
    public async Task<ErrorOr<IReadOnlyList<SaleListItemDto>>> Handle(
        GetSalesQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var sales = await saleRepository.GetByShopAsync(query.ShopId, cancellationToken);

        return sales
            .Select(s => new SaleListItemDto(
                s.Id,
                s.InvoiceNumber,
                s.CustomerId,
                s.PaymentMethod,
                s.SoldAt,
                s.PaidAmount,
                s.DueAmount,
                s.TotalAmount,
                s.TotalTaxAmount,
                s.CustomerName,
                s.CustomerPhone,
                s.Items.Count))
            .OrderByDescending(s => s.SoldAt)
            .ToList();
    }
}
