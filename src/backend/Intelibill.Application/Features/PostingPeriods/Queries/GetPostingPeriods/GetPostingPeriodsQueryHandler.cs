using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Application.Features.PostingPeriods.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PostingPeriods.Queries.GetPostingPeriods;

/// <summary>The twelve months of a fiscal year, April to March, with their audit trail. Owner or manager.</summary>
public sealed class GetPostingPeriodsQueryHandler(
    IShopRepository shopRepository,
    IPostingPeriodRepository periodRepository,
    IBusinessClock businessClock)
{
    public async Task<ErrorOr<PostingPeriodListDto>> HandleAsync(
        GetPostingPeriodsQuery query,
        CancellationToken cancellationToken)
    {
        var role = await shopRepository.GetActiveRoleAsync(query.UserId, query.ShopId, cancellationToken);
        if (role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.PostingPeriod.OwnerOrManagerRequired;

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var fiscalYear = query.FiscalYearStart;
        if (fiscalYear == 0)
        {
            var today = businessClock.Today(shop.BusinessTimeZoneId);
            fiscalYear = today.Month >= 4 ? today.Year : today.Year - 1;
        }

        if (fiscalYear is < 2000 or > 2999)
            return Errors.PostingPeriod.FiscalYearInvalid;

        var first = new DateOnly(fiscalYear, 4, 1);
        var last = first.AddYears(1).AddDays(-1);
        var periods = await periodRepository.GetRangeAsync(query.ShopId, first, last, cancellationToken);
        var events = periods.Count == 0
            ? []
            : await periodRepository.GetEventsAsync(query.ShopId, periods.Select(p => p.Id).ToList(), cancellationToken);

        var byStart = periods.ToDictionary(p => p.StartDate);
        var months = Enumerable.Range(0, 12)
            .Select(i => first.AddMonths(i))
            .Select(start => byStart.TryGetValue(start, out var period)
                ? PostingPeriodDtoMapper.Map(period, events)
                : PostingPeriodDtoMapper.Untouched(start))
            .ToList();

        return new PostingPeriodListDto(fiscalYear, months);
    }
}
