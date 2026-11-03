using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCase;

/// <summary>
/// One case with its envelope, people, available actions and audit trail. A case the caller may
/// not see — another shop's (hidden by row-level security) or, for staff, someone else's — is
/// answered exactly like a case that does not exist.
/// </summary>
public sealed class GetReconciliationCaseQueryHandler(
    IUserRepository userRepository,
    IReconciliationIssueRepository issueRepository,
    IOfflineSubmissionRepository submissionRepository,
    ReconciliationCaseMapper mapper)
{
    public async Task<ErrorOr<ReconciliationCaseDto>> HandleAsync(
        GetReconciliationCaseQuery query,
        CancellationToken cancellationToken)
    {
        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, query.ActorUserId, query.ShopId, cancellationToken);
        if (role.IsError)
            return role.Errors;

        var issue = await issueRepository.GetAsync(query.ShopId, query.CaseId, cancellationToken);
        if (issue is null)
            return Errors.Reconciliation.CaseNotFound;

        if (ReconciliationActor.VisibleCreator(role.Value, query.ActorUserId) is { } creator && issue.CreatedBy != creator)
            return Errors.Reconciliation.CaseNotFound;

        var submission = issue.OfflineSubmissionId is { } submissionId
            ? await submissionRepository.GetByIdAsync(submissionId, cancellationToken)
            : null;
        if (submission is not null && submission.ShopId != query.ShopId)
            submission = null;

        return await mapper.ToDetailAsync(issue, submission, role.Value, query.ActorUserId, cancellationToken);
    }
}
