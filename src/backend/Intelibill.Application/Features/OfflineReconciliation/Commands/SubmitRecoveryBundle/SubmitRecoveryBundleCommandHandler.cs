using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
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
/// <remarks>
/// <para>
/// Only an active owner or manager may submit, read live. The bundle must be the supported format,
/// belong to the caller's active shop and match its checksum, or nothing is read from it. The whole
/// submission runs inside <see cref="IFinancialOperationExecutor"/> keyed by the operation id, so a
/// resend replays the committed answer and the same key with another bundle answers 409.
/// </para>
/// <para>
/// Each invoice is checked in order: identity and structure, a lease on the bundle's device, and an
/// issuer who holds or held a membership. A failure rejects the row and stores nothing. A passing row
/// is stored as a <see cref="OfflineSubmissionSource.Recovery"/> envelope submitted by the operator
/// (an identical payload already received — say by the cashier's own sync — returns that first
/// envelope instead), then:
/// </para>
/// <list type="number">
/// <item>an envelope that has not posted and already has an unresolved case keeps that case: no second
/// case is filed, and the row reports it (<c>ReviewOpened</c> or <c>Escalated</c>);</item>
/// <item>an identical sale already posted links the envelope (<c>AlreadyPosted</c>); a different one
/// opens an idempotency conflict;</item>
/// <item>an invoice nobody can be vouched for is escalated at intake;</item>
/// <item>an inconsistent one opens an arithmetic case;</item>
/// <item>an issue time the lease cannot account for (before the lease, in the future, in another fiscal
/// year, after expiry — judged against the envelope's first receipt, as sync does) opens an
/// <c>IssueTimingReview</c> case with that code;</item>
/// <item>anything else, including a lease that simply expired before the server saw the invoice
/// (inherent to a late recovery), opens a <c>RecoveryReviewRequired</c> case.</item>
/// </list>
/// <para>
/// The invoices are read exactly as the sync endpoint maps them, so both paths stage one envelope alike.
/// </para>
/// </remarks>
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
    TimeProvider timeProvider)
{
    public const string BundleFormat = "intelibill.offline-recovery";
    public const int BundleVersion = 1;
    public const int MaxInvoices = 100;

    private static readonly SubmitRecoveryBundleCommandValidator Validator = new();

    // Web defaults with enums by name or number, as the sync endpoint's request binding reads them. The
    // endpoint maps no line type or service id (ReadInvoices resets both), so an invoice the web client's
    // sync mapper built stages exactly as that client's own sync request would.
    private static readonly JsonSerializerOptions InvoiceSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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

        // Recovering another user's invoices is an owner or manager decision, checked against the live membership.
        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
        if (role.IsError || role.Value is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Recovery.NotAuthorized;

        var bundle = command.Bundle;
        if (!string.Equals(bundle.Format, BundleFormat, StringComparison.Ordinal) || bundle.Version != BundleVersion)
            return Errors.Recovery.BundleFormatUnsupported;

        if (bundle.ShopId != command.ShopId)
            return Errors.Recovery.BundleShopMismatch;

        var checksum = ComputeChecksum(bundle.InvoicesJson);
        if (!string.Equals(checksum, bundle.Checksum, StringComparison.OrdinalIgnoreCase))
            return Errors.Recovery.BundleChecksumMismatch;

        var invoices = ReadInvoices(bundle.InvoicesJson);
        if (invoices is null)
            return Errors.Recovery.BundleInvalid;
        if (invoices.Count == 0)
            return Errors.Recovery.BundleEmpty;
        if (invoices.Count > MaxInvoices)
            return Errors.Recovery.BundleTooLarge;

        var context = new RecoveryContext(
            command.ShopId,
            command.ActorUserId,
            bundle.DeviceId.Trim(),
            FinancialOperationHasher.NormalizeKey(command.OperationId)!);

        // The checksum stands for the invoices, so device and checksum are the whole business payload.
        var requestHash = FinancialOperationHasher.Compute(new RecoveryPayload(context.DeviceId, checksum));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ShopId, FinancialOperationType.OfflineRecoverySubmission, context.OperationKey),
            command.ActorUserId,
            command.ShopId,
            requestHash,
            ct => RecoverAsync(context, invoices, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<RecoverySubmissionResultDto>> RecoverAsync(
        RecoveryContext context,
        List<OfflineSaleSyncCommand> invoices,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var results = new List<RecoveryInvoiceResultDto>(invoices.Count);
        foreach (var invoice in invoices)
            results.Add(await RecoverInvoiceAsync(context, invoice, now, cancellationToken));

        return new RecoverySubmissionResultDto(context.OperationKey, results);
    }

    private async Task<RecoveryInvoiceResultDto> RecoverInvoiceAsync(
        RecoveryContext context,
        OfflineSaleSyncCommand sale,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The same identity checks the sync endpoint applies, plus structure up front: a rejected row stores nothing.
        if (OfflineSaleSyncHelpers.CheckIdentity(sale, out var clientSaleId, out var invoiceNumber) is { } identityError)
            return Rejected(clientSaleId, invoiceNumber, identityError);

        var structure = salePoster.CheckStructure(sale);
        if (structure.IsError)
            return Rejected(clientSaleId, invoiceNumber, structure.FirstError);

        // The number must come from a range reserved for the bundle's own device.
        var lease = await invoiceLeaseRepository.FindReservedRangeAsync(context.ShopId, context.DeviceId, invoiceNumber, cancellationToken);
        if (lease is null)
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
                context.ShopId,
                context.DeviceId,
                clientSaleId,
                invoiceNumber,
                hash,
                OfflineSubmissionPayload.Serialize(sale),
                sale.SoldAt,
                now,
                OfflineSubmissionSource.Recovery,
                context.OperatorUserId,
                sale.IssuedByUserId,
                verifiedIssuer,
                lease.Id,
                context.OperationKey),
            cancellationToken);

        var held = new HeldInvoice(context, clientSaleId, invoiceNumber, submission.Id, now);

        // The envelope is already under review (the cashier's sync held it, or an earlier bundle did): that case decides it.
        if (submission.SaleId is null
            && await issueRepository.FindUnresolvedByEnvelopeAsync(context.ShopId, submission.Id, cancellationToken) is { } open)
        {
            return ExistingCaseResult(held, open);
        }

        var existing = await saleRepository.GetByClientSaleIdAsync(context.ShopId, context.DeviceId, clientSaleId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
                return await OpenCaseAsync(held, existing.Id, ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict, null, cancellationToken);

            // The invoice posted already (a late sync, an earlier recovery): link the evidence, nothing else to decide.
            if (submission.SaleId is null)
            {
                submission.LinkSale(existing.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new RecoveryInvoiceResultDto(clientSaleId, invoiceNumber, RecoveryInvoiceStatus.AlreadyPosted, null, existing.Id, []);
        }

        // Read from the stored envelope: an identical earlier envelope keeps its own provenance.
        if (submission.AttributableUserId is null)
        {
            var reason = submission.AssertedIssuerUserId is null
                ? Errors.OfflineReconciliation.ProvenanceUnknown
                : Errors.OfflineReconciliation.IssuerUnverified;
            return await EscalateCaseAsync(held, reason, cancellationToken);
        }

        if (OfflineSaleSyncHelpers.CheckArithmetic(sale) is { } arithmetic)
            return await OpenCaseAsync(held, null, arithmetic.IssueType, arithmetic.Error, arithmetic.Message, cancellationToken);

        // The same rule sync applies, against the first receipt. An expired lease alone is what every late
        // recovery looks like, so that verdict is the recovery review itself; any other anomaly is filed as is.
        if (OfflineIssueTimingPolicy.Evaluate(lease, sale.SoldAt, submission.ReceivedAt) is { } timing
            && timing.Code != Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code)
        {
            return await OpenCaseAsync(held, null, ReconciliationIssueType.IssueTimingReview, timing, null, cancellationToken);
        }

        return await OpenCaseAsync(
            held,
            null,
            ReconciliationIssueType.RecoveryReview,
            Errors.OfflineReconciliation.RecoveryReviewRequired,
            null,
            cancellationToken);
    }

    /// <summary>Files (or reuses) the open case for the envelope and reason, and saves it.</summary>
    private async Task<RecoveryInvoiceResultDto> OpenCaseAsync(
        HeldInvoice held,
        Guid? saleId,
        ReconciliationIssueType issueType,
        Error reason,
        string? message,
        CancellationToken cancellationToken)
    {
        var caseId = await FileCaseAsync(held, saleId, issueType, reason, message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return HeldResult(held, RecoveryInvoiceStatus.ReviewOpened, caseId, reason);
    }

    /// <summary>
    /// Files a provenance case and escalates it in the same save, recording the escalation by the
    /// operator. A reused case was escalated when first filed and is left as it is.
    /// </summary>
    private async Task<RecoveryInvoiceResultDto> EscalateCaseAsync(
        HeldInvoice held,
        Error reason,
        CancellationToken cancellationToken)
    {
        var context = held.Context;
        var caseId = await FileCaseAsync(held, null, ReconciliationIssueType.ProvenanceReview, reason, null, cancellationToken);

        // Finds a case added just now in the unit of work as well as an existing one.
        var issue = await issueRepository.GetByIdAsync(caseId, cancellationToken);
        if (issue is { Status: ReconciliationCaseStatus.Open } && !issue.Escalate(context.OperatorUserId, held.Now).IsError)
        {
            await caseEventRepository.AddAsync(
                ReconciliationCaseEvent.Record(
                    context.ShopId,
                    caseId,
                    ReconciliationAction.Escalate,
                    ReconciliationCaseStatus.Open,
                    ReconciliationCaseStatus.Escalated,
                    context.OperatorUserId,
                    reason.Description,
                    linkedOperationId: null,
                    context.OperationKey,
                    held.Now),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
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
            held.Context.ShopId,
            saleId,
            held.ClientSaleId,
            held.Context.DeviceId,
            issueType,
            reason.Code,
            message ?? reason.Description,
            held.Context.OperatorUserId,
            cancellationToken,
            held.InvoiceNumber,
            held.SubmissionId);

    private static RecoveryInvoiceResultDto ExistingCaseResult(HeldInvoice held, ReconciliationIssue open) =>
        new(
            held.ClientSaleId,
            held.InvoiceNumber,
            open.Status == ReconciliationCaseStatus.Escalated ? RecoveryInvoiceStatus.Escalated : RecoveryInvoiceStatus.ReviewOpened,
            open.Id,
            null,
            [new OfflineSaleSyncErrorDto(open.Code, open.Message)]);

    private static RecoveryInvoiceResultDto HeldResult(HeldInvoice held, string status, Guid caseId, Error reason) =>
        new(held.ClientSaleId, held.InvoiceNumber, status, caseId, null, [new OfflineSaleSyncErrorDto(reason.Code, reason.Description)]);

    private static RecoveryInvoiceResultDto Rejected(string clientSaleId, string invoiceNumber, Error error) =>
        new(clientSaleId, invoiceNumber, RecoveryInvoiceStatus.Rejected, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    private static string ComputeChecksum(string invoicesJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(invoicesJson)));

    /// <summary>The bundle's invoices, or null when the text is not an array of invoices the client could have built.</summary>
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

        // A null invoice or line can be neither checked nor hashed.
        if (invoices is null || invoices.Any(invoice => invoice is null || (invoice.Items?.Any(line => line is null) ?? false)))
            return null;

        return [.. invoices.Select(invoice => AsSyncEndpointReadsIt(invoice!))];
    }

    /// <summary>
    /// The sync endpoint maps neither <c>lineType</c> nor <c>serviceId</c> (each line binds as goods), and
    /// the idempotency hash excludes both; recovery reads them the same way so one envelope never stages
    /// differently by route.
    /// </summary>
    private static OfflineSaleSyncCommand AsSyncEndpointReadsIt(OfflineSaleSyncCommand invoice)
    {
        if (invoice.Items is null)
            return invoice;

        // The line record's own defaults, which is what the endpoint's mapping leaves in place.
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
        DateTimeOffset Now);

    /// <summary>The canonical business payload of one submission; the operation id itself is never part of it.</summary>
    private sealed record RecoveryPayload(string DeviceId, string Checksum);
}
