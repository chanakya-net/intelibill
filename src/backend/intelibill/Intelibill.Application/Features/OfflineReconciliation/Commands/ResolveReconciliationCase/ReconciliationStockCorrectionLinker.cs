using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using static Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase.ResolveReconciliationCaseCommandHandler;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

/// <summary>
/// Closes a stock-variance case against a covering stock increase, staging the shortage's batch and
/// item-total decrease and its catch-up <c>Out</c> movement.
/// </summary>
public sealed class ReconciliationStockCorrectionLinker(
    IReconciliationIssueRepository issueRepository,
    IInventoryAdjustmentRepository adjustmentRepository,
    IInventoryBatchRepository batchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISaleRepository saleRepository)
{
    internal async Task<ErrorOr<Effect>> LinkStockCorrectionAsync(
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
        if (batch is null || batch.ShopId != command.ShopId) return Errors.Inventory.BatchNotFound;
        if (batch.IsVoided) return Errors.Inventory.BatchAlreadyVoided;

        var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, itemId, cancellationToken);
        if (inventory is null) return Errors.Inventory.InventoryAggregateNotFound;

        var batchUpdate = batch.SubtractQuantity(unresolved, command.ActorUserId);
        if (batchUpdate.IsError) return batchUpdate.Errors;
        var inventoryUpdate = inventory.SubtractQuantity(unresolved, command.ActorUserId);
        if (inventoryUpdate.IsError) return inventoryUpdate.Errors;

        var reference = issue.InvoiceNumber;
        if (reference is null && issue.SaleId is { } saleId)
            reference = (await saleRepository.GetByIdAsync(saleId, command.ShopId, cancellationToken))?.InvoiceNumber;

        var movement = StockTransaction.Create(
            command.ShopId, itemId, batchId, StockTransactionType.Out, -unresolved, reference,
            $"Offline shortage catch-up: {request.Reason}", now, command.ActorUserId, command.ActorUserId);
        if (movement.IsError) return movement.Errors;

        await stockTransactionRepository.AddAsync(movement.Value, cancellationToken);
        return Close(issue, request, now, adjustmentId, movement.Value.Id);
    }
}
