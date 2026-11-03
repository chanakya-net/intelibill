using System.Text.Json;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation;

/// <summary>
/// Builds the case DTOs the console reads: names every person once, renders the frozen invoice
/// from the envelope and attaches the caller's available actions and the audit trail.
/// </summary>
public sealed class ReconciliationCaseMapper(
    IReconciliationCaseEventRepository caseEventRepository,
    IUserRepository userRepository)
{
    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ToSummariesAsync(
        IReadOnlyList<ReconciliationIssue> issues,
        CancellationToken cancellationToken)
    {
        var names = await userRepository.GetDisplayNamesAsync(
            issues.Select(i => i.CreatedBy).ToHashSet(),
            cancellationToken);

        return issues
            .Select(issue => new ReconciliationCaseSummaryDto(
                issue.Id,
                issue.Version,
                issue.InvoiceNumber,
                issue.ClientSaleId,
                issue.DeviceId,
                issue.IssueType.ToString(),
                issue.Status.ToString(),
                [issue.Code],
                issue.Message,
                issue.SaleId,
                issue.Resolution?.ToString(),
                issue.CreatedAt,
                Person(issue.CreatedBy, names),
                issue.ResolvedAt))
            .ToList();
    }

    /// <param name="issue">The case, as read (or as just saved).</param>
    /// <param name="submission">The case's envelope, when it has one.</param>
    /// <param name="role">The caller's live role, for the available actions.</param>
    /// <param name="actorUserId">The caller.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<ReconciliationCaseDto> ToDetailAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole role,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var events = await caseEventRepository.GetByCaseAsync(issue.ShopId, issue.Id, cancellationToken);

        var people = new HashSet<Guid> { issue.CreatedBy };
        if (issue.ResolvedBy is { } resolvedBy)
            people.Add(resolvedBy);
        foreach (var caseEvent in events)
            people.Add(caseEvent.ActorUserId);
        if (submission is not null)
        {
            people.Add(submission.SubmittedByUserId);
            if (submission.AssertedIssuerUserId is { } asserted)
                people.Add(asserted);
            if (submission.OriginalIssuerUserId is { } original)
                people.Add(original);
        }

        var names = await userRepository.GetDisplayNamesAsync(people, cancellationToken);

        return new ReconciliationCaseDto(
            issue.Id,
            issue.Version,
            issue.InvoiceNumber,
            issue.ClientSaleId,
            issue.DeviceId,
            issue.IssueType.ToString(),
            issue.Status.ToString(),
            [issue.Code],
            issue.Message,
            issue.ItemId,
            issue.InventoryBatchId,
            issue.PrintedQuantity,
            issue.AvailableQuantity,
            issue.ConsumedQuantity,
            issue.UnresolvedQuantity,
            issue.SaleId,
            issue.Resolution?.ToString(),
            issue.ResolutionReason,
            issue.LinkedOperationId,
            issue.ResolutionStockTransactionId,
            issue.CreatedAt,
            Person(issue.CreatedBy, names),
            issue.ResolvedAt,
            issue.ResolvedBy is { } resolver ? Person(resolver, names) : null,
            issue.OfflineSubmissionId is not null,
            submission is null ? null : ToSubmission(submission, names),
            ReconciliationActionPolicy.AvailableActions(issue, submission, role, actorUserId)
                .Select(action => action.ToString())
                .ToList(),
            events
                .Select(e => new ReconciliationCaseEventDto(
                    e.Action.ToString(),
                    e.FromStatus.ToString(),
                    e.ToStatus.ToString(),
                    Person(e.ActorUserId, names),
                    e.Reason,
                    e.LinkedOperationId,
                    e.OccurredAt))
                .ToList());
    }

    private static OfflineSubmissionDto? ToSubmission(OfflineSubmission submission, IReadOnlyDictionary<Guid, string> names)
    {
        var invoice = ReadInvoice(submission.PayloadJson);
        if (invoice is null)
            return null;

        return new OfflineSubmissionDto(
            submission.Id,
            submission.Source.ToString(),
            submission.ClaimedSoldAt,
            submission.ReceivedAt,
            submission.PayloadHash,
            submission.OriginalLeaseId,
            Person(submission.SubmittedByUserId, names),
            submission.AssertedIssuerUserId is { } asserted ? Person(asserted, names) : null,
            submission.OriginalIssuerUserId is { } original ? Person(original, names) : null,
            submission.RecoveryOperationId,
            invoice);
    }

    private static OfflineInvoiceSnapshotDto? ReadInvoice(string payloadJson)
    {
        OfflineSaleSyncCommand? sale;
        try
        {
            sale = OfflineSubmissionPayload.Deserialize(payloadJson);
        }
        catch (JsonException)
        {
            // The envelope is still evidence (hash, people, times); only its rendering is unavailable.
            return null;
        }

        if (sale is null)
            return null;

        return new OfflineInvoiceSnapshotDto(
            sale.InvoiceNumber,
            sale.SoldAt,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod.ToString(),
            sale.SubtotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.TotalTaxAmount,
            sale.TotalAmount,
            sale.PaidAmount,
            sale.DueAmount,
            (sale.Items ?? [])
                .Select(line => new OfflineInvoiceSnapshotLineDto(
                    line.ItemName,
                    line.Barcode,
                    line.IsServiceLine ? null : line.BatchNumber,
                    line.Quantity,
                    line.SalesPrice,
                    line.TaxRatePercent,
                    line.TaxAmount,
                    line.TotalAmount))
                .ToList());
    }

    private static ReconciliationPersonDto Person(Guid userId, IReadOnlyDictionary<Guid, string> names) =>
        new(userId, names.TryGetValue(userId, out var name) ? name : userId.ToString());
}
