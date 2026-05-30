using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Queries.GetDiscountRules;

public sealed class GetDiscountRulesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IDiscountRuleRepository discountRuleRepository)
{
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<PaginatedList<DiscountRuleListItemDto>>> Handle(
        GetDiscountRulesQuery query,
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

        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, MaxPageSize);
        var now = DateTimeOffset.UtcNow;

        var rules = await discountRuleRepository.GetByShopAsync(query.ShopId, cancellationToken);

        IEnumerable<DiscountRule> filtered = rules;

        filtered = ApplyStatus(filtered, query.Status, now);

        if (query.RuleType.HasValue)
            filtered = filtered.Where(r => r.RuleType == query.RuleType.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            filtered = filtered.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        filtered = ApplySort(filtered, query.Sort, now);

        var totalCount = filtered.Count();
        var items = filtered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListItemDto)
            .ToList();

        return new PaginatedList<DiscountRuleListItemDto>(items, totalCount, pageNumber, pageSize);
    }

    private static IEnumerable<DiscountRule> ApplyStatus(IEnumerable<DiscountRule> rules, string? status, DateTimeOffset now)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "active" => rules.Where(r => r.IsActive && (r.StartsAt is null || r.StartsAt <= now) && (r.EndsAt is null || r.EndsAt > now)),
            "upcoming" => rules.Where(r => r.IsActive && r.StartsAt is not null && r.StartsAt > now),
            "expired" => rules.Where(r => r.IsActive && r.EndsAt is not null && r.EndsAt <= now),
            "disabled" => rules.Where(r => !r.IsActive),
            null or "" or "all" => rules,
            _ => rules
        };
    }

    private static IEnumerable<DiscountRule> ApplySort(IEnumerable<DiscountRule> rules, string? sort, DateTimeOffset now)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "created_asc" => rules.OrderBy(r => r.CreatedAt),
            "created_desc" => rules.OrderByDescending(r => r.CreatedAt),
            "name_asc" => rules.OrderBy(r => r.Name),
            "name_desc" => rules.OrderByDescending(r => r.Name),
            "startsat_asc" => rules.OrderBy(r => r.StartsAt ?? DateTimeOffset.MinValue),
            "startsat_desc" => rules.OrderByDescending(r => r.StartsAt ?? DateTimeOffset.MinValue),
            "status" => rules.OrderBy(r => StatusRank(r, now)).ThenByDescending(r => r.CreatedAt),
            _ => rules.OrderByDescending(r => r.CreatedAt)
        };
    }

    private static int StatusRank(DiscountRule rule, DateTimeOffset now)
    {
        if (!rule.IsActive) return 3;
        if (rule.StartsAt is not null && rule.StartsAt > now) return 2;
        if (rule.EndsAt is not null && rule.EndsAt <= now) return 4;
        return 1;
    }

    private static DiscountRuleListItemDto ToListItemDto(DiscountRule rule) =>
        new(rule.Id, rule.RuleType, rule.Name, rule.IsActive, rule.StartsAt, rule.EndsAt, rule.CreatedAt);
}
