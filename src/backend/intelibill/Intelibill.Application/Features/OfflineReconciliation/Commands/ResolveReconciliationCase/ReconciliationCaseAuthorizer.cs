using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

/// <summary>
/// Decides whether the caller's live session and shop role may see a reconciliation case and take
/// the requested action on it, and loads the case for resolution once it is locked.
/// </summary>
public sealed class ReconciliationCaseAuthorizer(
    IUserRepository userRepository,
    IReconciliationIssueRepository issueRepository,
    ILiveSessionAuthority? liveSessionAuthority = null)
{
    internal async Task<ErrorOr<AuthorizedCase>> AuthorizeRequestAsync(
        ResolveReconciliationCaseCommand command,
        CancellationToken cancellationToken)
    {
        if (liveSessionAuthority is not null
            && await liveSessionAuthority.SessionDenialAsync(command.Session, command.ActorUserId, command.ShopId, cancellationToken) is { } sessionDenied)
        {
            return sessionDenied;
        }

        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
        if (role.IsError)
            return role.Errors;

        // Authorize target visibility before the executor so a replayed answer never reaches an unauthorized viewer.
        var visible = await issueRepository.GetAsync(command.ShopId, command.CaseId, cancellationToken);
        if (visible is null || !IsVisibleTo(visible, role.Value, command.ActorUserId))
            return Errors.Reconciliation.CaseNotFound;

        var action = ResolveReconciliationCaseCommandValidator.ParseAction(command.Action)!.Value;
        if (action != ReconciliationAction.Escalate && role.Value is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Reconciliation.ActionForbidden;

        return new AuthorizedCase(role.Value, visible, action);
    }

    internal async Task<Error?> AuthorizeLiveActorAsync(
        ResolveReconciliationCaseCommand command,
        ReconciliationAction action,
        CancellationToken cancellationToken)
    {
        if (liveSessionAuthority is not null
            && await liveSessionAuthority.SessionDenialAsync(command.Session, command.ActorUserId, command.ShopId, cancellationToken) is { } sessionDenied)
        {
            return sessionDenied;
        }

        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
        if (role.IsError)
            return role.FirstError;

        var visible = await issueRepository.GetAsync(command.ShopId, command.CaseId, cancellationToken);
        if (visible is null || !IsVisibleTo(visible, role.Value, command.ActorUserId))
            return Errors.Reconciliation.CaseNotFound;

        if (action != ReconciliationAction.Escalate && role.Value is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Reconciliation.ActionForbidden;

        return null;
    }

    internal async Task<ErrorOr<ShopRole>> ResolveLiveRoleAsync(
        ResolveReconciliationCaseCommand command,
        CancellationToken cancellationToken)
    {
        if (liveSessionAuthority is not null
            && await liveSessionAuthority.SessionDenialAsync(command.Session, command.ActorUserId, command.ShopId, cancellationToken) is { } sessionDenied)
        {
            return sessionDenied;
        }

        var liveRoleResult = await ReconciliationActor.ResolveRoleAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
        if (liveRoleResult.IsError)
            return liveRoleResult.Errors;
        return liveRoleResult.Value;
    }

    internal async Task<ErrorOr<ReconciliationIssue>> LoadResolvableIssueAsync(
        ResolveReconciliationCaseCommand command,
        ShopRole liveRole,
        CancellationToken cancellationToken)
    {
        var issue = await issueRepository.GetTrackedAsync(command.ShopId, command.CaseId, cancellationToken);
        if (issue is null || !IsVisibleTo(issue, liveRole, command.ActorUserId))
            return Errors.Reconciliation.CaseNotFound;

        if (issue.Status == ReconciliationCaseStatus.Resolved)
            return Errors.Reconciliation.CaseAlreadyResolved;

        if (issue.Version != command.ExpectedVersion)
            return Errors.Reconciliation.CaseVersionConflict;

        return issue;
    }

    private static bool IsVisibleTo(ReconciliationIssue issue, ShopRole role, Guid actorUserId) =>
        ReconciliationActor.VisibleCreator(role, actorUserId) is not { } creator || issue.CreatedBy == creator;

    internal sealed record AuthorizedCase(ShopRole Role, ReconciliationIssue Visible, ReconciliationAction Action);
}
