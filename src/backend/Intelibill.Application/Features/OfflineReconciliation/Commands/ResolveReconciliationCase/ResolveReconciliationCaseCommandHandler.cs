using System.Text.Json;
using Intelibill.Application.Features.Expiry;
using ErrorOr;
using Intelibill.Application.Common.Errors;
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
/// <remarks>
/// <para>
/// The action runs inside <see cref="IFinancialOperationExecutor"/>: a resend with the same operation
/// id replays the committed answer, and any refusal rolls back the reservation with everything else.
/// The case's effect (a catch-up movement, a link, a replayed sale), its transition and one
/// <see cref="ReconciliationCaseEvent"/> commit in a single save.
/// </para>
/// <para>
/// Two reviewers holding the same version cannot both win: resolutions of one case queue on its row
/// lock, so the later one reads the committed case and answers <c>Reconciliation.CaseAlreadyResolved</c>
/// or <c>Reconciliation.CaseVersionConflict</c>; PostgreSQL's <c>xmin</c> check at save remains the
/// backstop for any other writer, answered with the same version conflict and no effect.
/// </para>
/// </remarks>
public sealed class ResolveReconciliationCaseCommandHandler(
    IUserRepository userRepository,
    IReconciliationIssueRepository issueRepository,
    IReconciliationCaseEventRepository caseEventRepository,
    IOfflineSubmissionRepository submissionRepository,
    IInventoryAdjustmentRepository adjustmentRepository,
    IInventoryBatchRepository batchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISaleRepository saleRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IDiscountRuleRepository discountRuleRepository,
    IOfflineSalePoster salePoster,
    ReconciliationCaseMapper mapper,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    TimeProvider timeProvider,
    IOfflineExpiryAuthority expiryAuthority,
    OfflineTillReconciliationEligibility tillEligibility)
{
    private const string LinkedOperationIndex = "ix_reconciliation_issues_shop_id_linked_operation_id";
    private const string SaleInvoiceNumberIndex = "ix_sales_shop_id_invoice_number";
    private const string UniqueViolationState = "23505";
    private const string LockNotAvailableState = "55P03";

    private static readonly ResolveReconciliationCaseCommandValidator Validator = new();

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

        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
        if (role.IsError)
            return role.Errors;

        // Authorize the target before the executor, so a replayed answer never reaches someone who may not see the case.
        var visible = await issueRepository.GetAsync(command.ShopId, command.CaseId, cancellationToken);
        if (visible is null || !IsVisibleTo(visible, role.Value, command.ActorUserId))
            return Errors.Reconciliation.CaseNotFound;

        var request = new ResolutionRequest(
            command,
            ResolveReconciliationCaseCommandValidator.ParseAction(command.Action)!.Value,
            role.Value,
            command.Reason.Trim(),
            FinancialOperationHasher.NormalizeKey(command.OperationId)!);

        // Staff may only escalate their own case, including receipt replay. Target visibility
        // alone must not let a saved manager resolution bypass this action authorization.
        if (request.Action != ReconciliationAction.Escalate && role.Value is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Reconciliation.ActionForbidden;

        var requestHash = FinancialOperationHasher.Compute(new ResolutionPayload(
            command.CaseId,
            command.ExpectedVersion,
            request.Action.ToString(),
            request.Reason,
            command.LinkedOperationId, command.TillReconciliationEvidence));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ShopId, FinancialOperationType.ReconciliationResolution, request.OperationKey),
            command.ActorUserId,
            command.CaseId,
            requestHash,
            ct => ResolveAsync(request, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<ReconciliationCaseDto>> ResolveAsync(ResolutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Command;

        // Queue behind any resolution of this case already in flight, then read it as that one committed it.
        var locked = await issueRepository.LockForResolutionAsync(command.ShopId, command.CaseId, cancellationToken);
        if (locked == RowLockOutcome.TimedOut)
            return Errors.Reconciliation.ConcurrentChange;
        if (locked != RowLockOutcome.Acquired)
            return Errors.Reconciliation.CaseNotFound;

        var issue = await issueRepository.GetTrackedAsync(command.ShopId, command.CaseId, cancellationToken);
        if (issue is null || !IsVisibleTo(issue, request.Role, command.ActorUserId))
            return Errors.Reconciliation.CaseNotFound;

        if (issue.Status == ReconciliationCaseStatus.Resolved)
            return Errors.Reconciliation.CaseAlreadyResolved;

        if (issue.Version != command.ExpectedVersion)
            return Errors.Reconciliation.CaseVersionConflict;

        var submission = issue.OfflineSubmissionId is { } submissionId
            ? await submissionRepository.GetByIdAsync(submissionId, cancellationToken)
            : null;
        if (submission is not null && submission.ShopId != command.ShopId)
            submission = null;

        var now = timeProvider.GetUtcNow();
        var tillEligible = false;
        var expiryVerified = false;
        var expiryLinkVerified = false;
        if (issue.IssueType == ReconciliationIssueType.ExpiryReview && submission is not null)
        {
            var payload = ReadPayload(submission);
            expiryVerified = payload is not null && payload.Items.Where(x => !x.IsServiceLine).All(x => x.ExpiryEvidence is not null) &&
                !(await expiryAuthority.ValidateFrozenAsync(command.ShopId, payload, now, cancellationToken)).IsError;
            tillEligible = expiryVerified && submission.AttributableUserId is { } issuer &&
                await tillEligibility.CanReconcileAsync(command.ShopId, payload!, issuer, now, cancellationToken);
            var posted = command.LinkedOperationId is { } linked
                ? await saleRepository.GetByIdAsync(linked, command.ShopId, cancellationToken)
                : await saleRepository.GetByClientSaleIdAsync(command.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken);
            expiryLinkVerified = posted is not null && await expiryAuthority.VerifiesPostedSaleAsync(submission, posted, cancellationToken);
        }
        if (!ReconciliationActionPolicy.AvailableActions(issue, submission, request.Role, command.ActorUserId, expiryVerified, expiryLinkVerified, tillEligible).Contains(request.Action))
        {
            return request.Role is ShopRole.Owner or ShopRole.Manager
                ? Errors.Reconciliation.ActionNotAllowed
                : Errors.Reconciliation.ActionForbidden;
        }

        var fromStatus = issue.Status;
        ErrorOr<Effect> effect = request.Action switch
        {
            ReconciliationAction.Escalate => Escalate(issue, request, now),
            ReconciliationAction.AcknowledgeVariance => Close(issue, request, now, linkedOperationId: null, stockTransactionId: null),
            ReconciliationAction.LinkStockCorrection => await LinkStockCorrectionAsync(issue, request, now, cancellationToken),
            ReconciliationAction.LinkExistingSale => await LinkExistingSaleAsync(issue, submission, request, now, cancellationToken),
            ReconciliationAction.ReconcileTillSale => await RetryHistoricalLeaseAsync(issue, submission, request, now, cancellationToken),
            ReconciliationAction.RetryHistoricalLease => await RetryHistoricalLeaseAsync(issue, submission, request, now, cancellationToken),
            _ => Errors.Reconciliation.UnknownAction,
        };
        if (effect.IsError)
            return effect.Errors;

        await caseEventRepository.AddAsync(
            ReconciliationCaseEvent.Record(
                command.ShopId,
                issue.Id,
                request.Action,
                fromStatus,
                issue.Status,
                command.ActorUserId,
                request.Reason,
                effect.Value.LinkedOperationId,
                request.OperationKey,
                now),
            cancellationToken);

        var saved = await SaveAsync(cancellationToken);
        if (saved.IsError)
            return saved.Errors;

        return await mapper.ToDetailAsync(issue, submission, request.Role, command.ActorUserId, cancellationToken);
    }

    private static ErrorOr<Effect> Escalate(ReconciliationIssue issue, ResolutionRequest request, DateTimeOffset now)
    {
        var escalated = issue.Escalate(request.Command.ActorUserId, now);
        return escalated.IsError ? Errors.Reconciliation.ActionNotAllowed : new Effect(null);
    }

    private static ErrorOr<Effect> Close(
        ReconciliationIssue issue,
        ResolutionRequest request,
        DateTimeOffset now,
        Guid? linkedOperationId,
        Guid? stockTransactionId)
    {
        var closed = issue.Close(request.Action, request.Command.ActorUserId, now, request.Reason, linkedOperationId, stockTransactionId);
        return closed.IsError ? Errors.Reconciliation.CaseAlreadyResolved : new Effect(linkedOperationId);
    }

    /// <summary>
    /// Posts the stock the device printed but the server could not consume, once procurement evidence
    /// (a found-stock or other-gain adjustment of at least that quantity) exists for the same batch.
    /// The original sale and its movements are never touched.
    /// </summary>
    private async Task<ErrorOr<Effect>> LinkStockCorrectionAsync(
        ReconciliationIssue issue,
        ResolutionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (command.LinkedOperationId is not { } adjustmentId)
            return Errors.Reconciliation.LinkedOperationRequired;

        if (issue.UnresolvedQuantity is not > 0m || issue.ItemId is not { } itemId || issue.InventoryBatchId is not { } batchId)
            return Errors.Reconciliation.ActionNotAllowed;

        var unresolved = issue.UnresolvedQuantity.Value;
        var adjustment = await adjustmentRepository.GetByIdAsync(adjustmentId, cancellationToken);
        if (adjustment is null || !ReconciliationLinkRules.CoversShortage(adjustment, command.ShopId, itemId, batchId, unresolved))
            return Errors.Reconciliation.LinkedAdjustmentInvalid;

        if (await issueRepository.IsOperationLinkedAsync(command.ShopId, adjustmentId, cancellationToken))
            return Errors.Reconciliation.OperationAlreadyLinked;

        var batch = await batchRepository.GetByIdAsync(batchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ShopId)
            return Errors.Inventory.BatchNotFound;
        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, itemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        var batchUpdate = batch.SubtractQuantity(unresolved, command.ActorUserId);
        if (batchUpdate.IsError)
            return batchUpdate.Errors;

        var inventoryUpdate = inventory.SubtractQuantity(unresolved, command.ActorUserId);
        if (inventoryUpdate.IsError)
            return inventoryUpdate.Errors;

        var reference = issue.InvoiceNumber;
        if (reference is null && issue.SaleId is { } saleId)
            reference = (await saleRepository.GetByIdAsync(saleId, command.ShopId, cancellationToken))?.InvoiceNumber;

        var movement = StockTransaction.Create(
            command.ShopId,
            itemId,
            batchId,
            StockTransactionType.Out,
            -unresolved,
            reference,
            $"Offline shortage catch-up: {request.Reason}",
            now,
            command.ActorUserId,
            command.ActorUserId);
        if (movement.IsError)
            return movement.Errors;

        await stockTransactionRepository.AddAsync(movement.Value, cancellationToken);
        return Close(issue, request, now, adjustmentId, movement.Value.Id);
    }

    /// <summary>
    /// Links the case to a sale that already carries its invoice (<see cref="ReconciliationLinkRules.CheckSaleAsync"/>).
    /// The envelope is linked too, except the conflicting envelope of an idempotency conflict, which
    /// stays unlinked as evidence. A legacy case without an envelope is matched on its identity alone.
    /// </summary>
    private async Task<ErrorOr<Effect>> LinkExistingSaleAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ResolutionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (command.LinkedOperationId is not { } saleId)
            return Errors.Reconciliation.LinkedOperationRequired;

        var sale = await saleRepository.GetByIdAsync(saleId, command.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Reconciliation.SaleIdentityMismatch;

        if (await ReconciliationLinkRules.CheckSaleAsync(issue, submission, sale, submissionRepository, cancellationToken) is { } refused)
            return refused;

        if (issue.IssueType == ReconciliationIssueType.ExpiryReview && (submission is null || !await expiryAuthority.VerifiesPostedSaleAsync(submission, sale, cancellationToken)))
            return Errors.Reconciliation.ReplayRejected;

        if (submission is not null && !ReconciliationLinkRules.IsIdempotencyConflict(issue) && submission.LinkSale(sale.Id).IsError)
            return Errors.Reconciliation.SaleIdentityMismatch;

        issue.LinkSale(sale.Id);
        return Close(issue, request, now, sale.Id, stockTransactionId: null);
    }

    /// <summary>
    /// Posts the stored envelope as it was frozen, under its original invoice number, attributed to
    /// its verified issuer — or links the sale when that exact invoice has posted since.
    /// </summary>
    private async Task<ErrorOr<Effect>> RetryHistoricalLeaseAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ResolutionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (submission is null)
            return Errors.Reconciliation.EnvelopeMissing;

        // The policy offers the action only with an attributable issuer; this guards the action itself.
        if (submission.AttributableUserId is not { } issuer)
            return Errors.Reconciliation.ProvenanceUnverified;

        var lease = await invoiceLeaseRepository.FindReservedRangeAsync(command.ShopId, submission.DeviceId, submission.InvoiceNumber, cancellationToken);
        if (lease is null)
            return Errors.Sale.InvoiceLeaseNotFound;

        var existing = await saleRepository.GetByClientSaleIdAsync(command.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
                return Errors.Sale.IdempotencyConflict;

            if (issue.IssueType == ReconciliationIssueType.ExpiryReview && !await expiryAuthority.VerifiesPostedSaleAsync(submission, existing, cancellationToken))
                return Errors.Reconciliation.ReplayRejected;
            return LinkPostedSale(issue, submission, request, now, existing.Id);
        }

        if (await saleRepository.GetByInvoiceNumberAsync(command.ShopId, submission.InvoiceNumber, cancellationToken) is not null)
            return Errors.Sale.InvoiceNumberAlreadyUsed;

        var payload = ReadPayload(submission);
        if (payload is null)
            return Errors.Reconciliation.ReplayRejected;

        ReviewedTillContext? reviewedTill = null;
        if (request.Action == ReconciliationAction.ReconcileTillSale)
        {
            if (command.TillReconciliationEvidence is not { } evidence) return Errors.Till.SettlementEvidenceRequired;
            if (!string.Equals(OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, submission.DeviceId, payload), submission.PayloadHash, StringComparison.Ordinal) ||
                payload.ClientSaleId.Trim() != submission.ClientSaleId || payload.InvoiceNumber.Trim() != submission.InvoiceNumber || payload.SoldAt.UtcTicks / 10 != submission.ClaimedSoldAt.UtcTicks / 10)
                return Errors.Reconciliation.ReplayRejected;
            reviewedTill = new ReviewedTillContext(issue.Id, request.Reason, evidence);
        }

        var activeRules = await discountRuleRepository.GetActiveByShopAsync(command.ShopId, now, cancellationToken);
        var outcome = await salePoster.StageAsync(
            new OfflineSalePostingRequest(
                command.ShopId,
                submission.DeviceId,
                submission.ClientSaleId,
                payload,
                // The stored hash, so a later resend from the device replays this sale instead of conflicting.
                submission.PayloadHash,
                AttributedUserId: issuer,
                RecordedByUserId: command.ActorUserId,
                submission.Id,
                now,
                activeRules.ToDictionary(rule => rule.Id),
                lease,
                // Existing historical credit review retains its decision. A TillReview retry is
                // ordinary drawer recovery; its customer-credit variance must still be filed.
                CreditReviewed: request.Action == ReconciliationAction.RetryHistoricalLease && issue.IssueType != ReconciliationIssueType.TillReview,
                ReviewedTill: reviewedTill),
            cancellationToken);

        return outcome switch
        {
            OfflineSalePostingOutcome.Staged staged => LinkPostedSale(issue, submission, request, now, staged.Sale.Id),
            OfflineSalePostingOutcome.Rejected rejected => new List<Error> { Errors.Reconciliation.ReplayRejected, rejected.Error },
            OfflineSalePostingOutcome.Held held => new List<Error> { Errors.Reconciliation.ReplayRejected, held.Error },
            _ => Errors.Reconciliation.ReplayRejected,
        };
    }

    private static ErrorOr<Effect> LinkPostedSale(
        ReconciliationIssue issue,
        OfflineSubmission submission,
        ResolutionRequest request,
        DateTimeOffset now,
        Guid saleId)
    {
        if (submission.LinkSale(saleId).IsError)
            return Errors.Reconciliation.SaleIdentityMismatch;

        issue.LinkSale(saleId);
        return Close(issue, request, now, saleId, stockTransactionId: null);
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
            // Another resolution (or any write of the case or batch) committed first; nothing of this one is kept.
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
            // A row the resolution writes stayed locked past the budget; nothing was kept.
            return Errors.Reconciliation.ConcurrentChange;
        }
    }

    private static bool IsVisibleTo(ReconciliationIssue issue, ShopRole role, Guid actorUserId) =>
        ReconciliationActor.VisibleCreator(role, actorUserId) is not { } creator || issue.CreatedBy == creator;

    private static OfflineSaleSyncCommand? ReadPayload(OfflineSubmission submission)
    {
        try
        {
            return OfflineSubmissionPayload.Deserialize(submission.PayloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ResolutionRequest(
        ResolveReconciliationCaseCommand Command,
        ReconciliationAction Action,
        ShopRole Role,
        string Reason,
        string OperationKey);

    /// <summary>What the action linked, recorded on the case event.</summary>
    private sealed record Effect(Guid? LinkedOperationId);

    /// <summary>The canonical business payload of one resolution; the operation id itself is never part of it.</summary>
    private sealed record ResolutionPayload(
        Guid CaseId,
        uint ExpectedVersion,
        string Action,
        string Reason,
        Guid? LinkedOperationId,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        TillReconciliationEvidence? TillReconciliationEvidence);
}
