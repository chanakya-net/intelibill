using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCases;

/// <summary>
/// The review queue: one page of cases, newest first. Owners and managers see every case in the
/// shop; staff see only the cases they filed. Row-level security keeps other shops' cases out.
/// </summary>
public sealed class GetReconciliationCasesQueryHandler(
    IUserRepository userRepository,
    IReconciliationIssueRepository issueRepository,
    ReconciliationCaseMapper mapper)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<ReconciliationCasePageDto>> HandleAsync(
        GetReconciliationCasesQuery query,
        CancellationToken cancellationToken)
    {
        var filter = ParseStatus(query.Status);
        if (filter is null)
            return Errors.Reconciliation.InvalidStatusFilter;

        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, query.ActorUserId, query.ShopId, cancellationToken);
        if (role.IsError)
            return role.Errors;

        var page = Math.Max(query.Page, 1);
        var pageSize = query.PageSize < 1 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var creator = ReconciliationActor.VisibleCreator(role.Value, query.ActorUserId);

        var (items, totalCount) = await issueRepository.GetPageAsync(query.ShopId, filter.Value, creator, page, pageSize, cancellationToken);
        var unresolved = await issueRepository.CountUnresolvedAsync(query.ShopId, creator, cancellationToken);

        return new ReconciliationCasePageDto(
            await mapper.ToSummariesAsync(items, cancellationToken),
            page,
            pageSize,
            totalCount,
            unresolved);
    }

    private static ReconciliationCaseFilter? ParseStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "open" => ReconciliationCaseFilter.Open,
            "escalated" => ReconciliationCaseFilter.Escalated,
            "resolved" => ReconciliationCaseFilter.Resolved,
            "all" => ReconciliationCaseFilter.All,
            _ => null,
        };
}
