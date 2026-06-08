using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

public sealed class ReceivePurchaseOrderCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IPurchaseOrderReceiptRepository receiptRepository,
    IInboundInventoryLineProcessor inboundInventoryLineProcessor,
    IPurchaseOrderReceiptNumberGenerator receiptNumberGenerator,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        ReceivePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.PurchaseOrder.UserCannotMutatePurchaseOrder;

        if (command.Lines.Count == 0)
            return Errors.PurchaseOrder.ReceiptLineRequired;

        if (command.Lines.Select(line => line.PurchaseOrderLineId).Distinct().Count() != command.Lines.Count)
            return Errors.PurchaseOrder.DuplicateReceiptLine;

        var receivedAt = (command.ReceivedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var po = await purchaseOrderRepository.GetReceiptDetailForUpdateAsync(command.ActiveShopId, command.PurchaseOrderId, cancellationToken);
            if (po is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Errors.PurchaseOrder.NotFound;
            }

            if (po.Status is not (PurchaseOrderStatus.Placed or PurchaseOrderStatus.PartiallyReceived))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Errors.PurchaseOrder.CannotReceiveInvalidStatus;
            }

            var resolvedLines = new List<(ReceivePurchaseOrderLineInput Input, PurchaseOrderLine Line)>(command.Lines.Count);
            foreach (var lineInput in command.Lines)
            {
                var poLine = po.Lines.FirstOrDefault(l => l.Id == lineInput.PurchaseOrderLineId);
                if (poLine is null)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.ReceiptLineNotFound;
                }

                if (lineInput.Quantity <= 0)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.ReceiptQuantityInvalid;
                }

                if (lineInput.Quantity > poLine.RemainingQuantity)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.ReceiptQuantityOverRemaining;
                }

                resolvedLines.Add((lineInput, poLine));
            }

            var duplicateBatch = resolvedLines
                .GroupBy(item => new { item.Line.ItemId, BatchNumber = item.Input.BatchNumber.Trim() })
                .Any(group => group.Key.BatchNumber.Length > 0 && group.Count() > 1);
            if (duplicateBatch)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Errors.Inventory.BatchNumberAlreadyExists;
            }

            var receiptNumber = await receiptNumberGenerator.GenerateAsync(command.ActiveShopId, receivedAt.Year, cancellationToken);
            var receipt = PurchaseOrderReceipt.Create(
                command.ActiveShopId,
                po.Id,
                receiptNumber,
                receivedAt,
                command.ReferenceNumber,
                command.Notes,
                command.ActorUserId);

            var itemResolutionContext = new ItemResolutionContext();
            var inventoryUpdateContext = new InventoryUpdateContext();

            foreach (var (lineInput, poLine) in resolvedLines)
            {
                var inboundInput = new InboundInventoryLineInput(
                    poLine.ItemId,
                    poLine.Description,
                    lineInput.Barcode,
                    null,
                    string.Empty,
                    lineInput.BatchNumber,
                    lineInput.Quantity,
                    lineInput.TotalPurchaseCost,
                    lineInput.Mrp,
                    lineInput.SalesPrice,
                    lineInput.TaxRatePercent,
                    lineInput.TaxIncluded,
                    lineInput.PurchaseTaxIncluded,
                    lineInput.ExpiryDate,
                    lineInput.ManufacturingDate,
                    po.SupplierId,
                    command.ReferenceNumber,
                    command.Notes,
                    receivedAt,
                    null);

                var inboundResult = await inboundInventoryLineProcessor.ProcessAsync(
                    command.ActiveShopId,
                    inboundInput,
                    command.ActorUserId,
                    itemResolutionContext,
                    inventoryUpdateContext,
                    cancellationToken);

                if (inboundResult.IsError)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return inboundResult.Errors;
                }

                receipt.AddLine(
                    poLine.Id,
                    poLine.ItemId,
                    inboundResult.Value.Batch.Id,
                    inboundResult.Value.StockTransaction.Id,
                    lineInput.Quantity,
                    lineInput.TotalPurchaseCost,
                    lineInput.Mrp,
                    lineInput.SalesPrice,
                    lineInput.TaxRatePercent,
                    lineInput.TaxIncluded,
                    lineInput.PurchaseTaxIncluded);

                po.ApplyReceipt(poLine.Id, lineInput.Quantity);
            }
            po.AddReceipt(receipt);

            await receiptRepository.AddAsync(receipt, cancellationToken);
            purchaseOrderRepository.Update(po);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            return PurchaseOrderDtoMapper.ToDetail(po);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
