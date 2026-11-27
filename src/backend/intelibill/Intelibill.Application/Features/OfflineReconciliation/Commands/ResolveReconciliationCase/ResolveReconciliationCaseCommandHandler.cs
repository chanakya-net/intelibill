using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

/// <summary>
/// Takes one allowlisted action on a reconciliation case, exactly once per operation id and only
/// against the case version the reviewer was shown.
/// </summary>
public sealed class ResolveReconciliationCaseCommandHandler(
    ReconciliationCaseAuthorizer caseAuthorizer,
    IReconciliationIssueRepository issueRepository,
    IReconciliationCaseEventRepository caseEventRepository,
    IOfflineSubmissionRepository submissionRepository,
    ReconciliationStockCorrectionLinker stockCorrectionLinker,
    ReconciliationSaleLinker saleLinker,
    ReconciliationCaseMapper mapper,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    TimeProvider timeProvider,
    ReconciliationExpiryReviewVerifier expiryReviewVerifier,
    IShopLicenceFence? licenceFence = null,
    LicenseHistoryReviewAssessment? historyAssessment = null)
{
    private const string LinkedOperationIndex = "ix_reconciliation_issues_shop_id_linked_operation_id";
    private const string SaleInvoiceNumberIndex = "ix_sales_shop_id_invoice_number";
    private const string UniqueViolationState = "23505";
    private const string LockNotAvailableState = "55P03";

    private static readonly ResolveReconciliationCaseCommandValidator Validator = new();

    [LicenceAction(LicenceAction.Recovery)]
    public async Task<ErrorOr<ReconciliationCaseDto>> HandleAsync(
        ResolveReconciliationCaseCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
        {
            return validation.Errors
                .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
                .ToList();
        }

        // Authorize target visibility before the executor so a replayed answer never reaches an unauthorized viewer.
        var authorized = await caseAuthorizer.AuthorizeRequestAsync(command, cancellationToken);
        if (authorized.IsError)
            return authorized.Errors;

        var request = new ResolutionRequest(
            command,
            authorized.Value.Action,
            authorized.Value.Role,
            command.Reason.Trim(),
            FinancialOperationHasher.NormalizeKey(command.OperationId)!,
            authorized.Value.Visible.DeviceId);

        var requestHash = FinancialOperationHasher.Compute(new ResolutionPayload(
            command.CaseId,
            command.ExpectedVersion,
            request.Action.ToString(),
            request.Reason,
            command.LinkedOperationId,
            command.TillReconciliationEvidence));

        ShopLicenceLease? activeLicence = null;
        var scope = new FinancialOperationScope(command.ShopId, FinancialOperationType.ReconciliationResolution, request.OperationKey)
        {
            SuspensionAction = LicenceAction.Recovery,
            RecheckReplayAuthority = ct => caseAuthorizer.AuthorizeLiveActorAsync(command, request.Action, ct),
            RecheckBeforeCommit = async ct =>
            {
                if (await caseAuthorizer.AuthorizeLiveActorAsync(command, request.Action, ct) is { } actorDenied)
                    return actorDenied;
                return activeLicence?.CurrentDecision().EnforcedDenial();
            },
        };

        return await operationExecutor.ExecuteAsync(
            scope,
            command.ActorUserId,
            command.CaseId,
            requestHash,
            ct => ResolveAsync(request, lease => activeLicence = lease, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<ReconciliationCaseDto>> ResolveAsync(
        ResolutionRequest request,
        Action<ShopLicenceLease?> captureLicence,
        CancellationToken cancellationToken)
    {
        var command = request.Command;

        // Lock order across all writers: operation receipt -> device lock -> case lock -> licence fence -> period/expiry/till/business.
        var lockResult = await LockDeviceAndCaseAsync(request, cancellationToken);
        if (lockResult.IsError)
            return lockResult.Errors;

        var liveRoleResult = await caseAuthorizer.ResolveLiveRoleAsync(command, cancellationToken);
        if (liveRoleResult.IsError)
            return liveRoleResult.Errors;
        var liveRole = liveRoleResult.Value;

        var issueResult = await caseAuthorizer.LoadResolvableIssueAsync(command, liveRole, cancellationToken);
        if (issueResult.IsError)
            return issueResult.Errors;
        var issue = issueResult.Value;

        var fence = await EnterLicenceFenceAsync(command, captureLicence, cancellationToken);
        if (fence.IsError)
            return fence.Errors;
        var fencedLicence = fence.Value.Lease;

        var submission = issue.OfflineSubmissionId is { } submissionId
            ? await submissionRepository.GetByIdAsync(submissionId, cancellationToken)
            : null;
        if (submission is not null && submission.ShopId != command.ShopId)
            submission = null;

        var lease = await saleLinker.ResolveLeaseAsync(command.ShopId, submission, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var expiryChecks = await expiryReviewVerifier.VerifyExpiryReviewAsync(issue, submission, command, now, cancellationToken);

        LicenseHistoryCaseEvaluation? historyEval = historyAssessment is null
            ? null
            : await historyAssessment.EvaluateAsync(issue, submission, lease, now, fencedLicence, command.LinkedOperationId, cancellationToken);
        if (historyEval?.Denial is { } historyDenial)
            return historyDenial;

        var isHistoryCase = ReconciliationActionPolicy.IsLicenceHistoryCase(issue, historyEval?.HasSavedObservations ?? false);
        if (CheckActionAvailable(issue, submission, liveRole, request, expiryChecks, historyEval) is { } unavailable)
            return unavailable;

        var fromStatus = issue.Status;
        ErrorOr<Effect> effect = request.Action switch
        {
            ReconciliationAction.Escalate => Escalate(issue, request, now),
            ReconciliationAction.AcknowledgeVariance => Close(issue, request, now, linkedOperationId: null, stockTransactionId: null),
            ReconciliationAction.LinkStockCorrection => await stockCorrectionLinker.LinkStockCorrectionAsync(issue, request, now, cancellationToken),
            ReconciliationAction.LinkExistingSale => await saleLinker.LinkExistingSaleAsync(issue, submission, isHistoryCase, request, now, cancellationToken),
            ReconciliationAction.ReconcileTillSale => await saleLinker.RetryHistoricalLeaseUnderPostingLocksAsync(issue, submission, lease, isHistoryCase, fencedLicence, request, now, cancellationToken),
            ReconciliationAction.RetryHistoricalLease => await saleLinker.RetryHistoricalLeaseUnderPostingLocksAsync(issue, submission, lease, isHistoryCase, fencedLicence, request, now, cancellationToken),
            _ => Errors.Reconciliation.UnknownAction,
        };
        if (effect.IsError)
            return effect.Errors;

        await caseEventRepository.AddAsync(
            ReconciliationCaseEvent.Record(
                command.ShopId, issue.Id, request.Action, fromStatus, issue.Status,
                command.ActorUserId, request.Reason, effect.Value.LinkedOperationId, request.OperationKey, now),
            cancellationToken);

        var saved = await SaveAsync(cancellationToken);
        if (saved.IsError)
            return saved.Errors;

        return await mapper.ToDetailAsync(issue, submission, liveRole, command.ActorUserId, cancellationToken, fencedLicence);
    }

    private async Task<ErrorOr<Success>> LockDeviceAndCaseAsync(
        ResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (!string.IsNullOrWhiteSpace(request.DeviceId)
            && await submissionRepository.LockDeviceIntakeAsync(command.ShopId, request.DeviceId, cancellationToken) == RowLockOutcome.TimedOut)
        {
            return Errors.Reconciliation.ConcurrentChange;
        }

        var locked = await issueRepository.LockForResolutionAsync(command.ShopId, command.CaseId, cancellationToken);
        if (locked == RowLockOutcome.TimedOut)
            return Errors.Reconciliation.ConcurrentChange;
        if (locked != RowLockOutcome.Acquired)
            return Errors.Reconciliation.CaseNotFound;

        return Result.Success;
    }

    private async Task<ErrorOr<FencedLicence>> EnterLicenceFenceAsync(
        ResolveReconciliationCaseCommand command,
        Action<ShopLicenceLease?> captureLicence,
        CancellationToken cancellationToken)
    {
        ShopLicenceLease? fencedLicence = null;
        if (licenceFence is not null)
        {
            var entered = await licenceFence.EnterAsync(command.ShopId, LicenceAction.Recovery, cancellationToken);
            if (entered.IsError) return entered.Errors;
            if (entered.Value.CurrentDecision().EnforcedDenial() is { } licenceDenial) return licenceDenial;
            fencedLicence = entered.Value;
            captureLicence(fencedLicence);
        }

        return new FencedLicence(fencedLicence);
    }

    private static Error? CheckActionAvailable(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole liveRole,
        ResolutionRequest request,
        ExpiryChecks expiryChecks,
        LicenseHistoryCaseEvaluation? historyEval)
    {
        var availableActions = ReconciliationActionPolicy.AvailableActions(
            issue, submission, liveRole, request.Command.ActorUserId,
            expiryChecks.ExpiryVerified, expiryChecks.ExpiryLinkVerified, expiryChecks.TillEligible,
            historyEval?.HasSavedObservations ?? false,
            historyEval?.RetryEligible ?? false,
            historyEval?.LinkEligible ?? false);

        if (!availableActions.Contains(request.Action))
        {
            return liveRole is ShopRole.Owner or ShopRole.Manager
                ? Errors.Reconciliation.ActionNotAllowed
                : Errors.Reconciliation.ActionForbidden;
        }

        return null;
    }

    private static ErrorOr<Effect> Escalate(ReconciliationIssue issue, ResolutionRequest request, DateTimeOffset now)
    {
        var escalated = issue.Escalate(request.Command.ActorUserId, now);
        return escalated.IsError ? Errors.Reconciliation.ActionNotAllowed : new Effect(null);
    }

    internal static ErrorOr<Effect> Close(
        ReconciliationIssue issue,
        ResolutionRequest request,
        DateTimeOffset now,
        Guid? linkedOperationId,
        Guid? stockTransactionId)
    {
        var closed = issue.Close(request.Action, request.Command.ActorUserId, now, request.Reason, linkedOperationId, stockTransactionId);
        return closed.IsError ? Errors.Reconciliation.CaseAlreadyResolved : new Effect(linkedOperationId);
    }

    private async Task<ErrorOr<Success>> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.Reconciliation.CaseVersionConflict;
        }
        catch (DbUpdateException ex) when (OfflineSaleSyncHelpers.ContainsExceptionText(ex, UniqueViolationState))
        {
            if (OfflineSaleSyncHelpers.ContainsExceptionText(ex, LinkedOperationIndex))
                return Errors.Reconciliation.OperationAlreadyLinked;
            if (OfflineSaleSyncHelpers.ContainsExceptionText(ex, SaleInvoiceNumberIndex))
                return Errors.Sale.InvoiceNumberAlreadyUsed;
            return Errors.Reconciliation.ConcurrentChange;
        }
        catch (DbUpdateException ex) when (OfflineSaleSyncHelpers.ContainsExceptionText(ex, LockNotAvailableState))
        {
            return Errors.Reconciliation.ConcurrentChange;
        }
    }

    internal sealed record ResolutionRequest(
        ResolveReconciliationCaseCommand Command,
        ReconciliationAction Action,
        ShopRole Role,
        string Reason,
        string OperationKey,
        string DeviceId);

    internal sealed record Effect(Guid? LinkedOperationId);

    private sealed record FencedLicence(ShopLicenceLease? Lease);

    internal sealed record ExpiryChecks(bool TillEligible, bool ExpiryVerified, bool ExpiryLinkVerified);

    private sealed record ResolutionPayload(
        Guid CaseId,
        uint ExpectedVersion,
        string Action,
        string Reason,
        Guid? LinkedOperationId,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        TillReconciliationEvidence? TillReconciliationEvidence);
}
