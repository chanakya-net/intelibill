using ErrorOr;
using Intelibill.Application.Common.Errors;
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
    IOfflineSalePoster salePoster,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private const string StatusCreated = "created";
    private const string StatusSyncedWithWarnings = "SyncedWithWarnings";

    public async Task<ErrorOr<OfflineSalesSyncResponseDto>> HandleAsync(
        SyncOfflineSalesCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null) return Errors.Auth.UserNotFound;

        var isMember = actor.ShopMemberships.Any(sm => sm.ShopId == command.ShopId);
        if (!isMember) return Errors.Shop.MembershipNotFound;

        var deviceId = command.DeviceId.Trim();
        var now = timeProvider.GetUtcNow();
        var activeDiscountRules = await discountRuleRepository.GetActiveByShopAsync(command.ShopId, now, cancellationToken);
        var activeDiscountRulesById = activeDiscountRules.ToDictionary(rule => rule.Id);

        var results = new List<OfflineSaleSyncResultDto>(command.Sales.Count);
        foreach (var sale in command.Sales)
        {
            results.Add(await SyncSaleAsync(command, sale, deviceId, now, activeDiscountRulesById, cancellationToken));
        }

        return new OfflineSalesSyncResponseDto(results);
    }

    private async Task<OfflineSaleSyncResultDto> SyncSaleAsync(
        SyncOfflineSalesCommand command,
        OfflineSaleSyncCommand sale,
        string deviceId,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById,
        CancellationToken cancellationToken)
    {
        if (OfflineSaleSyncHelpers.CheckIdentity(sale, out var normalizedId, out var invNum) is { } identityError)
            return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, identityError);

        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, deviceId, sale);
        var lease = await invoiceLeaseRepository.FindReservedRangeAsync(command.ShopId, deviceId, invNum, cancellationToken);
        var (assertedIssuer, verifiedIssuer) = await ResolveIssuerAsync(command, deviceId, sale.IssuedByUserId, cancellationToken);

        // Stored before any check can hold the invoice; an identical resend gets the first row back,
        // so its ReceivedAt is the first receipt the timing rule trusts.
        var submission = await offlineSubmissionRepository.RecordAsync(
            OfflineSubmission.Record(
                command.ShopId,
                deviceId,
                normalizedId,
                invNum,
                hash,
                OfflineSubmissionPayload.Serialize(sale),
                sale.SoldAt,
                now,
                OfflineSubmissionSource.Sync,
                command.ActorUserId,
                assertedIssuer,
                verifiedIssuer,
                lease?.Id,
                recoveryOperationId: null),
            cancellationToken);

        var hold = new HeldCase(command.ShopId, normalizedId, deviceId, invNum, submission.Id, command.ActorUserId);

        var existing = await saleRepository.GetByClientSaleIdAsync(command.ShopId, deviceId, normalizedId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
                return await HoldAsync(hold, existing.Id, ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict, cancellationToken);

            if (submission.SaleId is null)
            {
                // A sale posted before envelopes existed (or whose envelope was lost to a race) adopts this one.
                submission.LinkSale(existing.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return OfflineSaleSyncHelpers.BuildDuplicateResult(normalizedId, existing);
        }

        var structure = salePoster.CheckStructure(sale);
        if (structure.IsError) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, structure.FirstError);

        // The web client never sends another user's invoices; one that arrives is reviewed, never posted as the submitter's.
        if (sale.IssuedByUserId is { } issuedBy && issuedBy != command.ActorUserId)
            return await HoldAsync(hold, null, ReconciliationIssueType.ProvenanceReview, Errors.OfflineReconciliation.IssuerNotSubmitter, cancellationToken);

        if (lease is null)
            return await HoldAsync(hold, null, ReconciliationIssueType.InvoiceConflict, Errors.Sale.InvoiceLeaseNotFound, cancellationToken);

        if (OfflineIssueTimingPolicy.Evaluate(lease, sale.SoldAt, submission.ReceivedAt) is { } timingError)
            return await HoldAsync(hold, null, ReconciliationIssueType.IssueTimingReview, timingError, cancellationToken);

        if (await saleRepository.GetByInvoiceNumberAsync(command.ShopId, invNum, cancellationToken) is not null)
            return await HoldAsync(hold, null, ReconciliationIssueType.InvoiceConflict, Errors.Sale.InvoiceNumberAlreadyUsed, cancellationToken);

        var outcome = await salePoster.StageAsync(
            new OfflineSalePostingRequest(
                command.ShopId,
                deviceId,
                normalizedId,
                sale,
                hash,
                AttributedUserId: command.ActorUserId,
                RecordedByUserId: command.ActorUserId,
                submission.Id,
                now,
                activeDiscountRulesById),
            cancellationToken);

        if (outcome is OfflineSalePostingOutcome.Rejected rejected)
            return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, rejected.Error);
        if (outcome is OfflineSalePostingOutcome.Held held)
            return await HoldAsync(hold, null, held.IssueType, held.Error, cancellationToken, held.Message, held.Mismatches);

        var staged = (OfflineSalePostingOutcome.Staged)outcome;
        var saleEntity = staged.Sale;
        submission.LinkSale(saleEntity.Id);

        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex)
        {
            unitOfWork.ClearChanges();
            var concurrent = await saleRepository.GetByClientSaleIdAsync(command.ShopId, deviceId, normalizedId, cancellationToken);
            if (concurrent is null)
            {
                var saveErr = OfflineSaleSyncHelpers.GetSaveFailureError(ex);
                if (saveErr.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code)
                    return await HoldAsync(hold, null, ReconciliationIssueType.InvoiceConflict, saveErr, cancellationToken);
                return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, saveErr);
            }
            if (!string.Equals(concurrent.RequestHash, hash, StringComparison.Ordinal))
                return await HoldAsync(hold, concurrent.Id, ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict, cancellationToken);

            // The concurrent winner received the same envelope row and links it with its own sale.
            return OfflineSaleSyncHelpers.BuildDuplicateResult(normalizedId, concurrent);
        }

        var warnings = staged.Warnings;
        return new OfflineSaleSyncResultDto(normalizedId, warnings.Count > 0 ? StatusSyncedWithWarnings : StatusCreated, saleEntity.Id, saleEntity.InvoiceNumber, []) { Warnings = warnings };
    }

    /// <summary>
    /// Decision 4/5 issuer fields for the envelope: the assertion as sent, and the issuer only when
    /// verified — the submitter themselves, or a current or former member with history on this device.
    /// </summary>
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

    /// <summary>Files (or reuses) the case for a held invoice, with its invoice number and envelope, and saves it.</summary>
    private async Task<OfflineSaleSyncResultDto> HoldAsync(
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
            hold.RecordedByUserId,
            cancellationToken,
            hold.InvoiceNumber,
            hold.SubmissionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OfflineSaleSyncHelpers.BuildNeedsReviewResult(hold.ClientSaleId, error, caseId, mismatches);
    }

    private sealed record HeldCase(
        Guid ShopId,
        string ClientSaleId,
        string DeviceId,
        string InvoiceNumber,
        Guid SubmissionId,
        Guid RecordedByUserId);
}
