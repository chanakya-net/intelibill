using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;

/// <summary>
/// Accepts a support recovery bundle exported from a retired or revoked cashier's browser and files
/// every verifiable invoice in it for review. Nothing is posted here: an owner or manager approves
/// each case later (<c>RetryHistoricalLease</c>), which replays the stored envelope as frozen.
/// </summary>
public sealed class SubmitRecoveryBundleCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IOfflineSubmissionRepository submissionRepository,
    ISaleRepository saleRepository,
    IReconciliationIssueRepository issueRepository,
    IReconciliationCaseEventRepository caseEventRepository,
    IOfflineSaleVarianceAnalyzer varianceAnalyzer,
    IOfflineSalePoster salePoster,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    TimeProvider timeProvider,
    IOfflineExpiryAuthority expiryAuthority,
    IShopLicenceFence? licenceFence = null,
    IOfflineLicenceHistoryAuthority? licenceHistory = null,
    ILiveSessionAuthority? liveSessionAuthority = null)
{
    public const string BundleFormat = "intelibill.offline-recovery";
    public const int BundleVersion = 1;
    public const int MaxInvoices = 100;

    private static readonly SubmitRecoveryBundleCommandValidator Validator = new();

    private static readonly JsonSerializerOptions InvoiceSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [LicenceAction(LicenceAction.Recovery)]
    public async Task<ErrorOr<RecoverySubmissionResultDto>> HandleAsync(
        SubmitRecoveryBundleCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
        {
            return validation.Errors
                .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
                .ToList();
        }

        if (await AuthorizeOperatorAsync(command.ShopId, command.ActorUserId, command.Session, cancellationToken) is { } denied)
            return denied;

        var bundle = command.Bundle;
        if (!string.Equals(bundle.Format, BundleFormat, StringComparison.Ordinal) || bundle.Version != BundleVersion)
            return Errors.Recovery.BundleFormatUnsupported;
        if (bundle.ShopId != command.ShopId)
            return Errors.Recovery.BundleShopMismatch;

        var checksum = ComputeChecksum(bundle.InvoicesJson);
        if (!string.Equals(checksum, bundle.Checksum, StringComparison.OrdinalIgnoreCase))
            return Errors.Recovery.BundleChecksumMismatch;

        var invoices = ReadInvoices(bundle.InvoicesJson);
        if (invoices is null) return Errors.Recovery.BundleInvalid;
        if (invoices.Count == 0) return Errors.Recovery.BundleEmpty;
        if (invoices.Count > MaxInvoices) return Errors.Recovery.BundleTooLarge;

        var context = new RecoveryContext(
            command.ShopId,
            command.ActorUserId,
            bundle.DeviceId.Trim(),
            FinancialOperationHasher.NormalizeKey(command.OperationId)!);

        var requestHash = FinancialOperationHasher.Compute(new RecoveryPayload(context.DeviceId, checksum));
        var scope = new FinancialOperationScope(command.ShopId, FinancialOperationType.OfflineRecoverySubmission, context.OperationKey)
        {
            SuspensionAction = LicenceAction.Recovery,
            RecheckReplayAuthority = ct => AuthorizeOperatorAsync(command.ShopId, command.ActorUserId, command.Session, ct),
            RecheckBeforeCommit = ct => AuthorizeOperatorAsync(command.ShopId, command.ActorUserId, command.Session, ct),
        };

        return await operationExecutor.ExecuteAsync(
            scope,
            command.ActorUserId,
            command.ShopId,
            requestHash,
            ct => RecoverAsync(context, invoices, ct),
            cancellationToken);
    }

    private async Task<Error?> AuthorizeOperatorAsync(Guid shopId, Guid actorUserId, SessionClaims? session, CancellationToken ct)
    {
        if (liveSessionAuthority is not null && await liveSessionAuthority.SessionDenialAsync(session, actorUserId, shopId, ct) is { } sessionDenied)
            return sessionDenied;

        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, actorUserId, shopId, ct);
        return role.IsError || role.Value is not (ShopRole.Owner or ShopRole.Manager)
            ? Errors.Recovery.NotAuthorized
            : null;
    }

    private async Task<ErrorOr<RecoverySubmissionResultDto>> RecoverAsync(
        RecoveryContext context,
        List<OfflineSaleSyncCommand> invoices,
        CancellationToken cancellationToken)
    {
        if (await submissionRepository.LockDeviceIntakeAsync(context.ShopId, context.DeviceId, cancellationToken) == RowLockOutcome.TimedOut)
            return Errors.Reconciliation.ConcurrentChange;

        ShopLicenceLease? fencedLicence = null;
        if (licenceFence is not null)
        {
            var entered = await licenceFence.EnterAsync(context.ShopId, LicenceAction.Recovery, cancellationToken);
            if (entered.IsError) return entered.Errors;
            if (entered.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;
            fencedLicence = entered.Value;
        }

        var now = timeProvider.GetUtcNow();
        var results = new List<RecoveryInvoiceResultDto>(invoices.Count);
        foreach (var invoice in invoices)
        {
            var outcome = await RecoverInvoiceAsync(context, invoice, now, fencedLicence, cancellationToken);
            if (outcome.IsError) return outcome.Errors;
            results.Add(outcome.Value);
        }

        return new RecoverySubmissionResultDto(context.OperationKey, results);
    }

    private async Task<ErrorOr<RecoveryInvoiceResultDto>> RecoverInvoiceAsync(
        RecoveryContext context,
        OfflineSaleSyncCommand sale,
        DateTimeOffset now,
        ShopLicenceLease? fencedLicence,
        CancellationToken cancellationToken)
    {
        if (OfflineSaleSyncHelpers.CheckIdentity(sale, out var clientSaleId, out var invoiceNumber) is { } identityError)
            return Rejected(clientSaleId, invoiceNumber, identityError);

        var structure = salePoster.CheckStructure(sale);
        if (structure.IsError)
            return Rejected(clientSaleId, invoiceNumber, structure.FirstError);

        var incomingLease = await invoiceLeaseRepository.FindReservedRangeAsync(context.ShopId, context.DeviceId, invoiceNumber, cancellationToken);
        if (incomingLease is null)
            return Rejected(clientSaleId, invoiceNumber, Errors.Recovery.LeaseNotOwned);

        Guid? verifiedIssuer = null;
        if (sale.IssuedByUserId is { } asserted)
        {
            var standing = await OfflineIssuerVerification.AssessAsync(
                shopRepository, submissionRepository, context.ShopId, context.DeviceId, asserted, cancellationToken);
            if (standing == OfflineIssuerStanding.NeverMember)
                return Rejected(clientSaleId, invoiceNumber, Errors.Recovery.IssuerMismatch);
            if (standing == OfflineIssuerStanding.Verified)
                verifiedIssuer = asserted;
        }

        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(context.ShopId, context.DeviceId, sale);
        var submission = await submissionRepository.RecordAsync(
            OfflineSubmission.Record(
                context.ShopId, context.DeviceId, clientSaleId, invoiceNumber, hash,
                OfflineSubmissionPayload.Serialize(sale), sale.SoldAt, now, OfflineSubmissionSource.Recovery,
                context.OperatorUserId, sale.IssuedByUserId, verifiedIssuer, incomingLease.Id, context.OperationKey),
            cancellationToken);

        var held = new HeldInvoice(context, submission.ClientSaleId, submission.InvoiceNumber, submission.Id, submission.SubmittedByUserId, now);
        var storedSale = OfflineSubmissionPayload.TryReadValidated(context.ShopId, submission);
        if (storedSale is null)
        {
            var corrupted = await OpenLicenceHistoryAsync(held, Errors.OfflineLicenceHistory.EvidenceInvalid,
                new(Errors.OfflineLicenceHistory.EvidenceInvalid.Code, "PayloadCorrupted", "payloadJson"), cancellationToken);
            return corrupted;
        }

        var lease = await ResolveEffectiveLeaseAsync(context.ShopId, submission, incomingLease, cancellationToken);

        // Existing canonical history hold takes precedence immediately after receipt capture, including linked envelopes.
        var existingHistory = await issueRepository.FindCanonicalHistoryCaseAsync(
                context.ShopId, submission.Id, includeResolvedTypedCase: false, cancellationToken);
        if (existingHistory is not null)
        {
            var assess = await AssessLicenceAsync(fencedLicence, submission, storedSale, lease, now, existingHistoryHold: true, cancellationToken);
            if (assess is { Decision: OfflineLicenceHistoryDecision.Denied, Error: { } deniedErr })
                return deniedErr;
            if (assess is { Observation: { } obs })
            {
                await varianceAnalyzer.AttachLicenceObservationAsync(
                    existingHistory, submission.Id, obs, now, advanceVersionOnNewObservation: true, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return ExistingCaseResult(held, existingHistory);
        }

        // Prior non-history unresolved case on an unlinked envelope keeps that case; attach any licence conflict to it.
        if (submission.SaleId is null)
        {
            var open = await issueRepository.FindUnresolvedPrePostingByEnvelopeAsync(context.ShopId, submission.Id, cancellationToken)
                ?? await issueRepository.FindUnresolvedByEnvelopeAsync(context.ShopId, submission.Id, cancellationToken);
            if (open is not null)
            {
                var assess = await AssessLicenceAsync(fencedLicence, submission, storedSale, lease, now, existingHistoryHold: false, cancellationToken);
                if (assess is { Decision: OfflineLicenceHistoryDecision.Denied, Error: { } deniedErr })
                    return deniedErr;
                if (assess is { Decision: OfflineLicenceHistoryDecision.Hold, Observation: { } obs })
                {
                    await varianceAnalyzer.AttachLicenceObservationAsync(
                        open, submission.Id, obs, now, advanceVersionOnNewObservation: true, cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                return ExistingCaseResult(held, open);
            }
        }

        var existing = await saleRepository.GetByClientSaleIdAsync(context.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
                return await OpenCaseAsync(held, existing.Id, ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict, null, null, cancellationToken);

            if (submission.SaleId is null)
            {
                submission.LinkSale(existing.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return new RecoveryInvoiceResultDto(submission.ClientSaleId, submission.InvoiceNumber, RecoveryInvoiceStatus.AlreadyPosted, null, existing.Id, []);
        }

        var historyAssess = await AssessLicenceAsync(fencedLicence, submission, storedSale, lease, now, existingHistoryHold: false, cancellationToken);
        if (historyAssess is { Decision: OfflineLicenceHistoryDecision.Denied, Error: { } historyDenied })
            return historyDenied;
        var pendingObs = historyAssess is { Decision: OfflineLicenceHistoryDecision.Hold } ? historyAssess.Observation : null;

        if (submission.AttributableUserId is null)
        {
            var reason = submission.AssertedIssuerUserId is null
                ? Errors.OfflineReconciliation.ProvenanceUnknown
                : Errors.OfflineReconciliation.IssuerUnverified;
            return await EscalateCaseAsync(held, reason, pendingObs, cancellationToken);
        }

        if (OfflineSaleSyncHelpers.CheckArithmetic(storedSale) is { } arithmetic)
            return await OpenCaseAsync(held, null, arithmetic.IssueType, arithmetic.Error, arithmetic.Message, pendingObs, cancellationToken);

        var expiry = await expiryAuthority.ValidateFrozenAsync(context.ShopId, storedSale, now, cancellationToken);
        if (expiry.IsError)
            return await OpenCaseAsync(held, null, ReconciliationIssueType.ExpiryReview, expiry.FirstError, null, pendingObs, cancellationToken);

        if (OfflineIssueTimingPolicy.Evaluate(lease, storedSale.SoldAt, submission.ReceivedAt) is { } timing
            && timing.Code != Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code)
        {
            return await OpenCaseAsync(held, null, ReconciliationIssueType.IssueTimingReview, timing, null, pendingObs, cancellationToken);
        }

        if (historyAssess is { Decision: OfflineLicenceHistoryDecision.Hold, Error: { } holdErr })
            return await OpenLicenceHistoryAsync(held, holdErr, historyAssess.Observation, cancellationToken);

        return await OpenCaseAsync(
            held, null, ReconciliationIssueType.RecoveryReview, Errors.OfflineReconciliation.RecoveryReviewRequired, null, null, cancellationToken);
    }

    private async Task<OfflineLicenceHistoryResult?> AssessLicenceAsync(
        ShopLicenceLease? fencedLicence,
        OfflineSubmission submission,
        OfflineSaleSyncCommand storedSale,
        InvoiceLease lease,
        DateTimeOffset now,
        bool existingHistoryHold,
        CancellationToken cancellationToken)
    {
        if (fencedLicence is not { } licence || licenceHistory is null)
            return null;

        var request = new OfflineSalePostingRequest(
            submission.ShopId,
            submission.DeviceId,
            submission.ClientSaleId,
            storedSale,
            submission.PayloadHash,
            AttributedUserId: submission.AttributableUserId ?? Guid.Empty,
            RecordedByUserId: submission.SubmittedByUserId,
            submission.Id,
            now,
            new Dictionary<Guid, DiscountRule>(),
            lease,
            FirstReceivedAt: submission.ReceivedAt,
            ReceivedPermitId: submission.LicencePermitId,
            ReceivedGrantId: submission.LicenceGrantId,
            IsExistingHistoryReview: existingHistoryHold,
            ExistingLicence: licence);
        return await licenceHistory.AssessAsync(licence, request, existingHistoryHold, cancellationToken);
    }

    private async Task<InvoiceLease> ResolveEffectiveLeaseAsync(
        Guid shopId,
        OfflineSubmission submission,
        InvoiceLease incomingLease,
        CancellationToken cancellationToken)
    {
        if (submission.OriginalLeaseId is { } leaseId && leaseId != incomingLease.Id)
        {
            var original = await invoiceLeaseRepository.GetByIdAsync(leaseId, cancellationToken);
            if (original is not null && original.ShopId == shopId)
                return original;
        }
        return incomingLease;
    }

    private async Task<RecoveryInvoiceResultDto> OpenLicenceHistoryAsync(
        HeldInvoice held,
        Error reason,
        OfflineLicenceObservationFact? observation,
        CancellationToken cancellationToken)
    {
        var issue = await varianceAnalyzer.OpenOrAttachLicenceHistoryAsync(
            held.Context.ShopId, null, held.ClientSaleId, held.Context.DeviceId, held.InvoiceNumber,
            held.SubmissionId, held.CaseCreatedByUserId, reason, observation, held.Now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var status = issue.Status == ReconciliationCaseStatus.Escalated
            ? RecoveryInvoiceStatus.Escalated
            : RecoveryInvoiceStatus.ReviewOpened;
        return new(held.ClientSaleId, held.InvoiceNumber, status, issue.Id, null, [new OfflineSaleSyncErrorDto(issue.Code, issue.Message)]);
    }

    private async Task<RecoveryInvoiceResultDto> OpenCaseAsync(
        HeldInvoice held,
        Guid? saleId,
        ReconciliationIssueType issueType,
        Error reason,
        string? message,
        OfflineLicenceObservationFact? licenceObservation,
        CancellationToken cancellationToken)
    {
        var caseId = await FileCaseAsync(held, saleId, issueType, reason, message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (licenceObservation is not null && await issueRepository.GetByIdAsync(caseId, cancellationToken) is { } issue)
        {
            await varianceAnalyzer.AttachLicenceObservationAsync(
                issue, held.SubmissionId, licenceObservation, held.Now, advanceVersionOnNewObservation: false, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return HeldResult(held, RecoveryInvoiceStatus.ReviewOpened, caseId, reason);
    }

    private async Task<RecoveryInvoiceResultDto> EscalateCaseAsync(
        HeldInvoice held,
        Error reason,
        OfflineLicenceObservationFact? licenceObservation,
        CancellationToken cancellationToken)
    {
        var context = held.Context;
        var caseId = await FileCaseAsync(held, null, ReconciliationIssueType.ProvenanceReview, reason, null, cancellationToken);

        var issue = await issueRepository.GetByIdAsync(caseId, cancellationToken);
        if (issue is { Status: ReconciliationCaseStatus.Open } && !issue.Escalate(context.OperatorUserId, held.Now).IsError)
        {
            await caseEventRepository.AddAsync(
                ReconciliationCaseEvent.Record(
                    context.ShopId, caseId, ReconciliationAction.Escalate,
                    ReconciliationCaseStatus.Open, ReconciliationCaseStatus.Escalated,
                    context.OperatorUserId, reason.Description, linkedOperationId: null, context.OperationKey, held.Now),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (licenceObservation is not null && issue is not null)
        {
            await varianceAnalyzer.AttachLicenceObservationAsync(
                issue, held.SubmissionId, licenceObservation, held.Now, advanceVersionOnNewObservation: false, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return HeldResult(held, RecoveryInvoiceStatus.Escalated, caseId, reason);
    }

    private Task<Guid> FileCaseAsync(
        HeldInvoice held,
        Guid? saleId,
        ReconciliationIssueType issueType,
        Error reason,
        string? message,
        CancellationToken cancellationToken) =>
        varianceAnalyzer.AddReviewIssueAsync(
            held.Context.ShopId, saleId, held.ClientSaleId, held.Context.DeviceId,
            issueType, reason.Code, message ?? reason.Description, held.CaseCreatedByUserId,
            cancellationToken, held.InvoiceNumber, held.SubmissionId);

    private static RecoveryInvoiceResultDto ExistingCaseResult(HeldInvoice held, ReconciliationIssue open) =>
        new(held.ClientSaleId, held.InvoiceNumber,
            open.Status == ReconciliationCaseStatus.Escalated ? RecoveryInvoiceStatus.Escalated : RecoveryInvoiceStatus.ReviewOpened,
            open.Id, null, [new OfflineSaleSyncErrorDto(open.Code, open.Message)]);

    private static RecoveryInvoiceResultDto HeldResult(HeldInvoice held, string status, Guid caseId, Error reason) =>
        new(held.ClientSaleId, held.InvoiceNumber, status, caseId, null, [new OfflineSaleSyncErrorDto(reason.Code, reason.Description)]);

    private static RecoveryInvoiceResultDto Rejected(string clientSaleId, string invoiceNumber, Error error) =>
        new(clientSaleId, invoiceNumber, RecoveryInvoiceStatus.Rejected, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    private static string ComputeChecksum(string invoicesJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(invoicesJson)));

    private static List<OfflineSaleSyncCommand>? ReadInvoices(string invoicesJson)
    {
        List<OfflineSaleSyncCommand?>? invoices;
        try
        {
            invoices = JsonSerializer.Deserialize<List<OfflineSaleSyncCommand?>>(invoicesJson, InvoiceSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (invoices is null || invoices.Any(invoice => invoice is null || (invoice.Items?.Any(line => line is null) ?? false)))
            return null;

        return [.. invoices.Select(invoice => AsSyncEndpointReadsIt(invoice!))];
    }

    private static OfflineSaleSyncCommand AsSyncEndpointReadsIt(OfflineSaleSyncCommand invoice)
    {
        if (invoice.Items is null) return invoice;
        return invoice with
        {
            Items = [.. invoice.Items.Select(line => line with { LineType = nameof(SaleLineType.Goods), ServiceId = null })],
        };
    }

    private sealed record RecoveryContext(Guid ShopId, Guid OperatorUserId, string DeviceId, string OperationKey);

    private sealed record HeldInvoice(
        RecoveryContext Context,
        string ClientSaleId,
        string InvoiceNumber,
        Guid SubmissionId,
        Guid CaseCreatedByUserId,
        DateTimeOffset Now);

    private sealed record RecoveryPayload(string DeviceId, string Checksum);
}
