using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed class GetProfitLossReportQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ProfitLossReportBuilder reportBuilder)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<ProfitLossReportResultDto>> Handle(
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-6);
        var pageNumber = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var report = await reportBuilder.BuildAsync(query.ShopId, from, to, query.Type, query.Search, cancellationToken);

        var pageItems = report.Items
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ProfitLossReportResultDto(
            pageItems,
            report.Items.Count,
            pageNumber,
            pageSize,
            report.Summary,
            new ProfitLossAppliedFiltersDto(report.From, report.To, report.Type, report.Search, pageNumber, pageSize));
    }
}
