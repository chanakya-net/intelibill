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

        if (command.Lines.Count != 1)
            return Errors.PurchaseOrder.ReceiptLineRequired;

        var po = await purchaseOrderRepository.GetReceiptDetailAsync(command.ActiveShopId, command.PurchaseOrderId, cancellationToken);
        if (po is null)
            return Errors.PurchaseOrder.NotFound;

        if (po.Status is not (PurchaseOrderStatus.Placed or PurchaseOrderStatus.PartiallyReceived))
            return Errors.PurchaseOrder.CannotReceiveInvalidStatus;

        var lineInput = command.Lines[0];
        var poLine = po.Lines.FirstOrDefault(l => l.Id == lineInput.PurchaseOrderLineId);
        if (poLine is null)
            return Errors.PurchaseOrder.ReceiptLineNotFound;

        if (lineInput.Quantity <= 0)
            return Errors.PurchaseOrder.ReceiptQuantityInvalid;

        if (lineInput.Quantity > poLine.RemainingQuantity)
            return Errors.PurchaseOrder.ReceiptQuantityOverRemaining;

        var receivedAt = (command.ReceivedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var receiptNumber = await receiptNumberGenerator.GenerateAsync(command.ActiveShopId, receivedAt.Year, cancellationToken);

            var inboundInput = new InboundInventoryLineInput(
                poLine.ItemId,
                poLine.Description,
                string.Empty,
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
                new ItemResolutionContext(),
                new InventoryUpdateContext(),
                cancellationToken);

            if (inboundResult.IsError)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return inboundResult.Errors;
            }

            var receipt = PurchaseOrderReceipt.Create(
                command.ActiveShopId,
                po.Id,
                receiptNumber,
                receivedAt,
                command.ReferenceNumber,
                command.Notes,
                command.ActorUserId);

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
