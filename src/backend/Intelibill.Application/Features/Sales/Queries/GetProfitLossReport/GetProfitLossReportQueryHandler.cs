using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed class GetProfitLossReportQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository)
{
    public async Task<ErrorOr<IReadOnlyList<ProfitLossReportItemDto>>> Handle(
        GetProfitLossReportQuery query,
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
            .Select(s =>
            {
                decimal totalCost = 0;
                decimal revenueBeforeTax = 0;
                decimal revenueAfterTax = 0;

                foreach (var item in s.Items)
                {
                    decimal basePrice = item.IsPriceIncludingTax
                        ? item.SalesPrice / (1 + item.TaxRatePercent / 100)
                        : item.SalesPrice;

                    decimal taxPerUnit = basePrice * (item.TaxRatePercent / 100);
                    decimal finalPrice = basePrice + taxPerUnit;

                    totalCost += item.CostPrice * item.Quantity;
                    revenueBeforeTax += basePrice * item.Quantity;
                    revenueAfterTax += finalPrice * item.Quantity;
                }

                decimal taxAmount = revenueAfterTax - revenueBeforeTax;
                return new ProfitLossReportItemDto(
                    s.Id,
                    s.InvoiceNumber,
                    s.SoldAt,
                    s.CustomerName,
                    totalCost,
                    revenueBeforeTax,
                    revenueAfterTax,
                    revenueBeforeTax - totalCost,
                    revenueBeforeTax - totalCost - taxAmount);
            })
            .OrderByDescending(s => s.SoldAt)
            .ToList();
    }
}
