using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.OfflineReconciliation;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>
/// Syncs a device's queued offline invoices. Each invoice is stored as an envelope before anything
/// else can hold or post it, so every held case points at the exact payload the server received.
/// </summary>
public sealed class SyncOfflineSalesCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IOfflineSubmissionRepository offlineSubmissionRepository,
    ISaleRepository saleRepository,
    IDiscountRuleRepository discountRuleRepository,
    IOfflineSaleVarianceAnalyzer varianceAnalyzer,
    IReconciliationIssueRepository reconciliationIssueRepository,
    IOfflineSalePoster salePoster,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IShopLicenceFence? licenceFence = null,
    IOfflineLicenceHistoryAuthority? licenceHistory = null)
{
    private const string StatusCreated = "created";
    private const string StatusSyncedWithWarnings = "SyncedWithWarnings";
    private const string StageSavepoint = "offline_sale_stage";

    [LicenceAction(LicenceAction.Recovery)]
    public async Task<ErrorOr<OfflineSalesSyncResponseDto>> HandleAsync(
        SyncOfflineSalesCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null) return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId && sm.IsActive);
        if (membership is null) return Errors.Shop.MembershipNotFound;

        var deviceId = command.DeviceId.Trim();
        var now = timeProvider.GetUtcNow();
        var activeDiscountRules = await discountRuleRepository.GetActiveByShopAsync(command.ShopId, now, cancellationToken);
        var activeDiscountRulesById = activeDiscountRules.ToDictionary(rule => rule.Id);

        var results = new List<OfflineSaleSyncResultDto>(command.Sales.Count);
        foreach (var sale in command.Sales)
        {
            results.Add(await SyncSaleAsync(command, membership.Role, sale, deviceId, now, activeDiscountRulesById, cancellationToken));
        }

        return new OfflineSalesSyncResponseDto(results);
    }

    private async Task<OfflineSaleSyncResultDto> SyncSaleAsync(
        SyncOfflineSalesCommand command,
        ShopRole actorRole,
        OfflineSaleSyncCommand sale,
        string deviceId,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById,
        CancellationToken cancellationToken)
    {
        if (OfflineSaleSyncHelpers.CheckIdentity(sale, out var normalizedId, out var invNum) is { } identityError)
            return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, identityError);

        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, deviceId, sale);
        var incomingLease = await invoiceLeaseRepository.FindReservedRangeAsync(command.ShopId, deviceId, invNum, cancellationToken);
        var (assertedIssuer, verifiedIssuer) = await ResolveIssuerAsync(command, deviceId, sale.IssuedByUserId, cancellationToken);

        var candidate = OfflineSubmission.Record(
            command.ShopId, deviceId, normalizedId, invNum, hash, OfflineSubmissionPayload.Serialize(sale),
            sale.SoldAt, now, OfflineSubmissionSource.Sync, command.ActorUserId,
            assertedIssuer, verifiedIssuer, incomingLease?.Id, recoveryOperationId: null);

        var recorded = await CommitReceiptAsync(command.ShopId, deviceId, candidate, cancellationToken);
        if (recorded.IsError)
            return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, recorded.FirstError);

        return await ProcessCommittedReceiptAsync(
            command, actorRole, recorded.Value, incomingLease, now, activeDiscountRulesById, cancellationToken);
    }

    private async Task<ErrorOr<OfflineSubmission>> CommitReceiptAsync(
        Guid shopId,
        string deviceId,
        OfflineSubmission candidate,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var open = true;
        try
        {
            var lockOutcome = await offlineSubmissionRepository.LockDeviceIntakeAsync(shopId, deviceId, cancellationToken);
            if (lockOutcome == RowLockOutcome.TimedOut)
            {
                await AbandonAsync(cancellationToken);
                open = false;
                return Errors.Reconciliation.ConcurrentChange;
            }

            var stored = await offlineSubmissionRepository.RecordAsync(candidate, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            open = false;
            unitOfWork.ClearChanges();
            return stored;
        }
        finally
        {
            if (open)
                await AbandonAsync(CancellationToken.None);
        }
    }

    private async Task<OfflineSaleSyncResultDto> ProcessCommittedReceiptAsync(
        SyncOfflineSalesCommand command,
        ShopRole actorRole,
        OfflineSubmission initialReceipt,
        InvoiceLease? incomingLease,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var open = true;
        try
        {
            var lockOutcome = await offlineSubmissionRepository.LockDeviceIntakeAsync(command.ShopId, initialReceipt.DeviceId, cancellationToken);
            if (lockOutcome == RowLockOutcome.TimedOut)
            {
                await AbandonAsync(cancellationToken);
                open = false;
                return OfflineSaleSyncHelpers.BuildErrorResult(initialReceipt.ClientSaleId, Errors.Reconciliation.ConcurrentChange);
            }

            var submission = await offlineSubmissionRepository.GetTrackedByIdAsync(command.ShopId, initialReceipt.Id, cancellationToken)
                ?? initialReceipt;
            var hold = new HeldCase(
                command.ShopId, submission.ClientSaleId, submission.DeviceId, submission.InvoiceNumber,
                submission.Id, submission.SubmittedByUserId, actorRole, command.ActorUserId);

            var storedSale = ReadValidatedPayload(command.ShopId, submission);
            if (storedSale is null)
            {
                var corrupted = await HoldLicenceHistoryAsync(hold, Errors.OfflineLicenceHistory.EvidenceInvalid,
                    new(Errors.OfflineLicenceHistory.EvidenceInvalid.Code, "PayloadCorrupted", "payloadJson"), now, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return corrupted;
            }

            var lease = await ResolveEffectiveLeaseAsync(command.ShopId, submission, incomingLease, cancellationToken);

            // Existing canonical history hold wins immediately after authorized receipt capture, including linked envelopes.
            var existingHistory = await reconciliationIssueRepository.FindCanonicalHistoryCaseAsync(command.ShopId, submission.Id, cancellationToken);
            if (existingHistory is not null)
            {
                var outcome = await HandleExistingHistoryCaseAsync(
                    hold, existingHistory, submission, storedSale, lease, now, activeDiscountRulesById, cancellationToken);
                if (outcome.Commit)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);
                }
                else
                {
                    await AbandonAsync(cancellationToken);
                }
                open = false;
                return outcome.Result;
            }

            // The web client never sends another user's invoices; one that arrives is reviewed, never posted as the submitter's.
            if (storedSale.IssuedByUserId is { } issuedBy && issuedBy != command.ActorUserId)
            {
                var result = await HoldInTransactionAsync(hold, null, ReconciliationIssueType.ProvenanceReview,
                    Errors.OfflineReconciliation.IssuerNotSubmitter, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return result;
            }

            var existing = await saleRepository.GetByClientSaleIdAsync(command.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
                {
                    var conflict = await HoldInTransactionAsync(hold, existing.Id, ReconciliationIssueType.ValidationConflict,
                        Errors.Sale.IdempotencyConflict, cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);
                    open = false;
                    return conflict;
                }

                if (submission.SaleId is null)
                {
                    submission.LinkSale(existing.Id);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return OfflineSaleSyncHelpers.BuildDuplicateResult(submission.ClientSaleId, existing);
            }

            var structure = salePoster.CheckStructure(storedSale);
            if (structure.IsError)
            {
                await AbandonAsync(cancellationToken);
                open = false;
                return OfflineSaleSyncHelpers.BuildErrorResult(submission.ClientSaleId, structure.FirstError);
            }

            if (lease is null)
            {
                var noLease = await HoldInTransactionAsync(hold, null, ReconciliationIssueType.InvoiceConflict,
                    Errors.Sale.InvoiceLeaseNotFound, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return noLease;
            }

            var timingError = OfflineIssueTimingPolicy.Evaluate(lease, storedSale.SoldAt, submission.ReceivedAt);
            if (timingError is { } independentTiming && independentTiming.Code != Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code)
            {
                var timingHeld = await HoldInTransactionAsync(hold, null, ReconciliationIssueType.IssueTimingReview,
                    independentTiming, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return timingHeld;
            }

            if (await saleRepository.GetByInvoiceNumberAsync(command.ShopId, submission.InvoiceNumber, cancellationToken) is not null)
            {
                var duplicateNum = await HoldInTransactionAsync(hold, null, ReconciliationIssueType.InvoiceConflict,
                    Errors.Sale.InvoiceNumberAlreadyUsed, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return duplicateNum;
            }

            // Acquire licence fence once under the device lock so historical assessment runs before any generic hold.
            ShopLicenceLease? fencedLicence = null;
            if (licenceFence is not null)
            {
                var entered = await licenceFence.EnterAsync(command.ShopId, LicenceAction.Recovery, cancellationToken);
                if (entered.IsError)
                {
                    await AbandonAsync(cancellationToken);
                    open = false;
                    return OfflineSaleSyncHelpers.BuildErrorResult(submission.ClientSaleId, entered.FirstError);
                }
                fencedLicence = entered.Value;
            }

            var pending = await reconciliationIssueRepository.FindUnresolvedPrePostingByEnvelopeAsync(command.ShopId, submission.Id, cancellationToken)
                ?? await reconciliationIssueRepository.FindUnresolvedByEnvelopeAsync(command.ShopId, submission.Id, cancellationToken);

            var postingRequest = new OfflineSalePostingRequest(
                command.ShopId,
                submission.DeviceId,
                submission.ClientSaleId,
                storedSale,
                submission.PayloadHash,
                AttributedUserId: command.ActorUserId,
                RecordedByUserId: command.ActorUserId,
                submission.Id,
                now,
                activeDiscountRulesById,
                lease,
                FirstReceivedAt: submission.ReceivedAt,
                ReceivedPermitId: submission.LicencePermitId,
                ReceivedGrantId: submission.LicenceGrantId,
                IsExistingHistoryReview: false,
                ExistingLicence: fencedLicence);

            if (fencedLicence is { } activeLicence && licenceHistory is not null)
            {
                var assess = await licenceHistory.AssessAsync(activeLicence, postingRequest, existingHistoryHold: false, cancellationToken);
                if (assess is { Decision: OfflineLicenceHistoryDecision.Denied, Error: { } deniedErr })
                {
                    await varianceAnalyzer.OpenOrAttachLicenceHistoryAsync(
                        hold.ShopId, null, hold.ClientSaleId, hold.DeviceId, hold.InvoiceNumber, hold.SubmissionId,
                        hold.CaseCreatedByUserId, deniedErr, assess.Observation, now, cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);
                    open = false;
                    return OfflineSaleSyncHelpers.BuildErrorResult(submission.ClientSaleId, deniedErr);
                }

                if (assess is { Decision: OfflineLicenceHistoryDecision.Hold, Error: { } holdErr })
                {
                    if (pending is not null)
                    {
                        if (assess.Observation is not null)
                        {
                            await varianceAnalyzer.AttachLicenceObservationAsync(
                                pending, hold.SubmissionId, assess.Observation, now, advanceVersionOnNewObservation: true, cancellationToken);
                        }
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        await unitOfWork.CommitTransactionAsync(cancellationToken);
                        open = false;
                        return OfflineSaleSyncHelpers.BuildNeedsReviewResult(
                            hold.ClientSaleId, Error.Conflict(pending.Code, pending.Message), VisibleCaseId(hold, pending));
                    }

                    var heldHistory = await HoldLicenceHistoryAsync(hold, holdErr, assess.Observation, now, cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);
                    open = false;
                    return heldHistory;
                }
            }

            if (pending is not null && IsBlockingPendingHold(pending))
            {
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return OfflineSaleSyncHelpers.BuildNeedsReviewResult(
                    submission.ClientSaleId, Error.Conflict(pending.Code, pending.Message), VisibleCaseId(hold, pending));
            }

            if (timingError is { } historicalTiming)
            {
                var historicalLease = await HoldInTransactionAsync(hold, null, ReconciliationIssueType.IssueTimingReview,
                    historicalTiming, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                open = false;
                return historicalLease;
            }

            var stagedResult = await StageAndCommitAsync(hold, submission, postingRequest, now, cancellationToken);
            open = false;
            return stagedResult;
        }
        finally
        {
            if (open)
                await AbandonAsync(CancellationToken.None);
        }
    }

    private async Task<OfflineSaleSyncResultDto> StageAndCommitAsync(
        HeldCase hold,
        OfflineSubmission submission,
        OfflineSalePostingRequest postingRequest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await unitOfWork.CreateSavepointAsync(StageSavepoint, cancellationToken);
        var outcome = await salePoster.StageAsync(postingRequest, cancellationToken);

        if (outcome is OfflineSalePostingOutcome.Held held)
        {
            await RollbackStageSavepointAsync();
            var heldResult = held.IssueType == ReconciliationIssueType.LicenseHistoryReview || held.LicenceObservation is not null
                ? await HoldLicenceHistoryAsync(hold, held.Error, held.LicenceObservation, now, cancellationToken)
                : await HoldInTransactionAsync(hold, null, held.IssueType, held.Error, cancellationToken, held.Message, held.Mismatches);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return heldResult;
        }

        if (outcome is OfflineSalePostingOutcome.Rejected rejected)
        {
            if (string.Equals(rejected.Error.Code, Errors.Licence.SecuritySuspended.Code, StringComparison.Ordinal))
            {
                await RollbackStageSavepointAsync();
                await varianceAnalyzer.OpenOrAttachLicenceHistoryAsync(
                    hold.ShopId, null, hold.ClientSaleId, hold.DeviceId, hold.InvoiceNumber, hold.SubmissionId,
                    hold.CaseCreatedByUserId, rejected.Error,
                    new(rejected.Error.Code, "SecuritySuspended", "restriction"), now, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                return OfflineSaleSyncHelpers.BuildErrorResult(hold.ClientSaleId, rejected.Error);
            }

            await AbandonAsync(cancellationToken);
            return OfflineSaleSyncHelpers.BuildErrorResult(hold.ClientSaleId, rejected.Error);
        }

        var staged = (OfflineSalePostingOutcome.Staged)outcome;
        var saleEntity = staged.Sale;
        submission.LinkSale(saleEntity.Id);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.ReleaseSavepointAsync(StageSavepoint, CancellationToken.None);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            var warnings = staged.Warnings;
            return new OfflineSaleSyncResultDto(
                hold.ClientSaleId,
                warnings.Count > 0 ? StatusSyncedWithWarnings : StatusCreated,
                saleEntity.Id,
                saleEntity.InvoiceNumber,
                [])
            { Warnings = warnings };
        }
        catch (DbUpdateException ex)
        {
            await RollbackStageSavepointAsync();
            var concurrent = await saleRepository.GetByClientSaleIdAsync(hold.ShopId, hold.DeviceId, hold.ClientSaleId, cancellationToken);
            if (concurrent is null)
            {
                var saveErr = OfflineSaleSyncHelpers.GetSaveFailureError(ex);
                if (saveErr.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code)
                {
                    var conflictResult = await HoldInTransactionAsync(hold, null, ReconciliationIssueType.InvoiceConflict, saveErr, cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);
                    return conflictResult;
                }

                await AbandonAsync(cancellationToken);
                return OfflineSaleSyncHelpers.BuildErrorResult(hold.ClientSaleId, saveErr);
            }

            if (!string.Equals(concurrent.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
            {
                var idem = await HoldInTransactionAsync(hold, concurrent.Id, ReconciliationIssueType.ValidationConflict,
                    Errors.Sale.IdempotencyConflict, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                return idem;
            }

            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return OfflineSaleSyncHelpers.BuildDuplicateResult(hold.ClientSaleId, concurrent);
        }
    }

    private async Task<(bool Commit, OfflineSaleSyncResultDto Result)> HandleExistingHistoryCaseAsync(
        HeldCase hold,
        ReconciliationIssue existingCase,
        OfflineSubmission submission,
        OfflineSaleSyncCommand storedSale,
        InvoiceLease? lease,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById,
        CancellationToken cancellationToken)
    {
        if (licenceFence is not null && licenceHistory is not null)
        {
            var entered = await licenceFence.EnterAsync(hold.ShopId, LicenceAction.Recovery, cancellationToken);
            if (entered.IsError)
                return (false, OfflineSaleSyncHelpers.BuildErrorResult(hold.ClientSaleId, entered.FirstError));

            var request = new OfflineSalePostingRequest(
                hold.ShopId, submission.DeviceId, submission.ClientSaleId, storedSale, submission.PayloadHash,
                AttributedUserId: submission.AttributableUserId ?? hold.CallerUserId,
                RecordedByUserId: hold.CallerUserId, submission.Id, now, activeDiscountRulesById, lease,
                FirstReceivedAt: submission.ReceivedAt, ReceivedPermitId: submission.LicencePermitId,
                ReceivedGrantId: submission.LicenceGrantId, IsExistingHistoryReview: true, ExistingLicence: entered.Value);
            var assess = await licenceHistory.AssessAsync(entered.Value, request, existingHistoryHold: true, cancellationToken);
            if (assess is { Observation: { } obs })
            {
                await varianceAnalyzer.AttachLicenceObservationAsync(
                    existingCase, submission.Id, obs, now, advanceVersionOnNewObservation: true, cancellationToken);
            }
            if (assess is { Decision: OfflineLicenceHistoryDecision.Denied, Error: { } deniedErr })
                return (true, OfflineSaleSyncHelpers.BuildErrorResult(hold.ClientSaleId, deniedErr));
        }

        return (true, OfflineSaleSyncHelpers.BuildNeedsReviewResult(
            hold.ClientSaleId, Error.Conflict(existingCase.Code, existingCase.Message), VisibleCaseId(hold, existingCase)));
    }

    private async Task<InvoiceLease?> ResolveEffectiveLeaseAsync(
        Guid shopId,
        OfflineSubmission submission,
        InvoiceLease? incomingLease,
        CancellationToken cancellationToken)
    {
        if (submission.OriginalLeaseId is { } leaseId)
        {
            if (incomingLease is not null && incomingLease.Id == leaseId)
                return incomingLease;
            var original = await invoiceLeaseRepository.GetByIdAsync(leaseId, cancellationToken);
            if (original is not null && original.ShopId == shopId)
                return original;
        }

        return incomingLease ?? await invoiceLeaseRepository.FindReservedRangeAsync(shopId, submission.DeviceId, submission.InvoiceNumber, cancellationToken);
    }

    private async Task RollbackStageSavepointAsync()
    {
        await unitOfWork.RollbackToSavepointAsync(StageSavepoint, CancellationToken.None);
        await unitOfWork.ReleaseSavepointAsync(StageSavepoint, CancellationToken.None);
        unitOfWork.ClearChanges();
    }

    private async Task AbandonAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.RollbackTransactionAsync(cancellationToken);
        unitOfWork.ClearChanges();
    }

    private async Task<(Guid? Asserted, Guid? Verified)> ResolveIssuerAsync(
        SyncOfflineSalesCommand command,
        string deviceId,
        Guid? issuedByUserId,
        CancellationToken cancellationToken)
    {
        if (issuedByUserId is not { } asserted)
            return (null, null);

        if (asserted == command.ActorUserId)
            return (asserted, asserted);

        var standing = await OfflineIssuerVerification.AssessAsync(
            shopRepository, offlineSubmissionRepository, command.ShopId, deviceId, asserted, cancellationToken);
        return (asserted, standing == OfflineIssuerStanding.Verified ? asserted : null);
    }

    private async Task<OfflineSaleSyncResultDto> HoldLicenceHistoryAsync(
        HeldCase hold,
        Error error,
        OfflineLicenceObservationFact? observation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var issue = await varianceAnalyzer.OpenOrAttachLicenceHistoryAsync(
            hold.ShopId, null, hold.ClientSaleId, hold.DeviceId, hold.InvoiceNumber, hold.SubmissionId,
            hold.CaseCreatedByUserId, error, observation, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OfflineSaleSyncHelpers.BuildNeedsReviewResult(
            hold.ClientSaleId, Error.Conflict(issue.Code, issue.Message), VisibleCaseId(hold, issue));
    }

    private async Task<OfflineSaleSyncResultDto> HoldInTransactionAsync(
        HeldCase hold,
        Guid? saleId,
        ReconciliationIssueType issueType,
        Error error,
        CancellationToken cancellationToken,
        string? message = null,
        IReadOnlyList<OfflineArithmeticMismatch>? mismatches = null)
    {
        var caseId = await varianceAnalyzer.AddReviewIssueAsync(
            hold.ShopId,
            saleId,
            hold.ClientSaleId,
            hold.DeviceId,
            issueType,
            error.Code,
            message ?? error.Description,
            hold.CaseCreatedByUserId,
            cancellationToken,
            hold.InvoiceNumber,
            hold.SubmissionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var visibleId = ReconciliationActor.VisibleCreator(hold.CallerRole, hold.CallerUserId) is not { } visibleCreator
            || hold.CaseCreatedByUserId == visibleCreator
            ? (Guid?)caseId
            : null;
        return OfflineSaleSyncHelpers.BuildNeedsReviewResult(hold.ClientSaleId, error, visibleId, mismatches);
    }

    private static Guid? VisibleCaseId(HeldCase hold, ReconciliationIssue issue) =>
        ReconciliationActor.VisibleCreator(hold.CallerRole, hold.CallerUserId) is not { } visibleCreator
        || issue.CreatedBy == visibleCreator
            ? issue.Id
            : null;

    private static bool IsBlockingPendingHold(ReconciliationIssue pending) =>
        pending.IssueType is ReconciliationIssueType.ClosedPeriodReview
            or ReconciliationIssueType.TillReview
            or ReconciliationIssueType.ExpiryReview
            or ReconciliationIssueType.LicenseHistoryReview
        || Errors.OfflineLicenceHistory.IsHistoryCode(pending.Code);

    private static OfflineSaleSyncCommand? ReadValidatedPayload(Guid shopId, OfflineSubmission submission)
    {
        try
        {
            var sale = OfflineSubmissionPayload.Deserialize(submission.PayloadJson);
            if (sale is null) return null;
            var recomputed = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, submission.DeviceId, sale);
            return string.Equals(recomputed, submission.PayloadHash, StringComparison.Ordinal) ? sale : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record HeldCase(
        Guid ShopId,
        string ClientSaleId,
        string DeviceId,
        string InvoiceNumber,
        Guid SubmissionId,
        Guid CaseCreatedByUserId,
        ShopRole CallerRole,
        Guid CallerUserId);
}
