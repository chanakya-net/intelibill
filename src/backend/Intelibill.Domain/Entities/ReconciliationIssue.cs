using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class ReconciliationIssue : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid? SaleId { get; private set; }
    public string ClientSaleId { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public ReconciliationIssueType IssueType { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? InventoryBatchId { get; private set; }
    public decimal? PrintedQuantity { get; private set; }
    public decimal? AvailableQuantity { get; private set; }
    public decimal? ConsumedQuantity { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool IsResolved { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public Guid CreatedBy { get; private set; }
    public ReconciliationCaseStatus Status { get; private set; } = ReconciliationCaseStatus.Open;
    public string? InvoiceNumber { get; private set; }
    public Guid? OfflineSubmissionId { get; private set; }

    /// <summary>The action that closed the case.</summary>
    public ReconciliationAction? Resolution { get; private set; }

    public string? ResolutionReason { get; private set; }
    public Guid? LinkedOperationId { get; private set; }
    public Guid? ResolutionStockTransactionId { get; private set; }

    /// <summary>Row version (PostgreSQL xmin); a resolution is accepted only against the version it was read at.</summary>
    public uint Version { get; private set; }

    /// <summary>Printed minus consumed for a stock variance that printed more than it consumed; otherwise null.</summary>
    public decimal? UnresolvedQuantity =>
        IssueType == ReconciliationIssueType.StockVariance && PrintedQuantity is { } printed && ConsumedQuantity is { } consumed
            ? Math.Max(0m, printed - consumed)
            : null;

    private ReconciliationIssue() { }

    public static ReconciliationIssue Create(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid createdBy,
        Guid? itemId = null,
        Guid? inventoryBatchId = null,
        decimal? printedQuantity = null,
        decimal? availableQuantity = null,
        decimal? consumedQuantity = null,
        string? invoiceNumber = null,
        Guid? offlineSubmissionId = null)
    {
        return new ReconciliationIssue
        {
            ShopId = shopId,
            SaleId = saleId,
            ClientSaleId = clientSaleId.Trim(),
            DeviceId = deviceId.Trim(),
            IssueType = issueType,
            ItemId = itemId,
            InventoryBatchId = inventoryBatchId,
            PrintedQuantity = printedQuantity,
            AvailableQuantity = availableQuantity,
            ConsumedQuantity = consumedQuantity,
            Code = code.Trim(),
            Message = message.Trim(),
            IsResolved = false,
            CreatedBy = createdBy,
            Status = ReconciliationCaseStatus.Open,
            InvoiceNumber = invoiceNumber?.Trim(),
            OfflineSubmissionId = offlineSubmissionId,
        };
    }

    public void Resolve(Guid resolvedBy, DateTimeOffset resolvedAt)
    {
        IsResolved = true;
        Status = ReconciliationCaseStatus.Resolved;
        ResolvedBy = resolvedBy;
        ResolvedAt = resolvedAt;
    }

    public void LinkSale(Guid saleId)
    {
        SaleId = saleId;
    }

    public void AttachEvidence(string invoiceNumber, Guid? offlineSubmissionId)
    {
        InvoiceNumber = invoiceNumber.Trim();
        OfflineSubmissionId = offlineSubmissionId;
    }

    /// <summary>Escalates an open case. The case stays unresolved.</summary>
    public ErrorOr<Success> Escalate(Guid actorUserId, DateTimeOffset at)
    {
        if (Status != ReconciliationCaseStatus.Open)
            return Error.Conflict("Reconciliation.CaseNotOpen", "Only an open case can be escalated.");

        Status = ReconciliationCaseStatus.Escalated;
        return Result.Success;
    }

    /// <summary>Closes an open or escalated case with the action that resolved it.</summary>
    public ErrorOr<Success> Close(
        ReconciliationAction action,
        Guid actorUserId,
        DateTimeOffset at,
        string reason,
        Guid? linkedOperationId,
        Guid? stockTransactionId)
    {
        if (Status == ReconciliationCaseStatus.Resolved)
            return Error.Conflict("Reconciliation.CaseAlreadyResolved", "The case is already resolved.");

        Resolve(actorUserId, at);
        Resolution = action;
        ResolutionReason = reason.Trim();
        LinkedOperationId = linkedOperationId;
        ResolutionStockTransactionId = stockTransactionId;
        return Result.Success;
    }
}
