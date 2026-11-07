using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Operations;
using Intelibill.Domain.ValueObjects;
using Intelibill.Application.Common.Time;
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
    IUnitOfWork unitOfWork,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    IItemRepository itemRepository,
    IFinancialOperationExecutor operationExecutor)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        ReceivePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.PurchaseOrder.UserCannotMutatePurchaseOrder;

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        if (command.OperationId is Guid operationId)
        {
            return await operationExecutor.ExecuteAsync<PurchaseOrderDetailDto>(
                new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.PurchaseOrderReceipt, operationId.ToString("D")),
                command.ActorUserId,
                command.PurchaseOrderId,
                FinancialOperationHasher.Compute(PurchaseOrderReceiptPayload.From(command)),
                ct => ReceiveCoreAsync(command, (command.ReceivedAt ?? businessClock.UtcNow).ToUniversalTime(), shop, ct),
                cancellationToken);
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await ReceiveCoreAsync(command, (command.ReceivedAt ?? businessClock.UtcNow).ToUniversalTime(), shop, cancellationToken);
            if (result.IsError)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                unitOfWork.ClearChanges();
                return result.Errors;
            }
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            unitOfWork.ClearChanges();
            throw;
        }
    }

    private async Task<ErrorOr<PurchaseOrderDetailDto>> ReceiveCoreAsync(
        ReceivePurchaseOrderCommand command, DateTimeOffset receivedAt, Shop shop, CancellationToken cancellationToken)
    {
        var validation = await new ReceivePurchaseOrderCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.Errors.Select(failure =>
            {
                var error = Error.Validation(failure.ErrorCode.StartsWith("PurchaseOrder.", StringComparison.Ordinal)
                    ? failure.ErrorCode : Errors.PurchaseOrder.InvalidReceiptValue.Code, failure.ErrorMessage);
                var path = failure.PropertyName;
                if (!path.StartsWith("Lines[", StringComparison.Ordinal)) return error;
                var bracket = path.IndexOf(']');
                var index = int.Parse(path.AsSpan(6, bracket - 6), System.Globalization.CultureInfo.InvariantCulture);
                var property = path[(bracket + 2)..];
                var field = char.ToLowerInvariant(property[0]) + property[1..];
                return PurchaseOrderReceiptErrors.ForLine(error, command.Lines[index].ClientLineId, index, field);
            }).ToList();
        }

        // The period fence precedes the order and line row locks.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ActiveShopId,
            businessClock.GetBusinessDate(receivedAt, shop.BusinessTimeZoneId), cancellationToken);
        if (fence.IsError) return fence.Errors;

        var po = await purchaseOrderRepository.GetReceiptDetailForUpdateAsync(command.ActiveShopId, command.PurchaseOrderId, cancellationToken);
        if (po is null) return Errors.PurchaseOrder.NotFound;
        if (po.Status is not (PurchaseOrderStatus.Placed or PurchaseOrderStatus.PartiallyReceived))
            return Errors.PurchaseOrder.CannotReceiveInvalidStatus;

        var errors = new List<Error>();
        var resolvedLines = new List<(ReceivePurchaseOrderLineInput Input, PurchaseOrderLine Line, int Index)>();
        for (var index = 0; index < command.Lines.Count; index++)
        {
            var input = command.Lines[index];
            var line = po.Lines.FirstOrDefault(l => l.Id == input.PurchaseOrderLineId);
            if (line is null)
                errors.Add(PurchaseOrderReceiptErrors.ForLine(Errors.PurchaseOrder.ReceiptLineNotFound, input.ClientLineId, index, "purchaseOrderLineId"));
            else
                resolvedLines.Add((input, line, index));
        }

        foreach (var group in resolvedLines.GroupBy(l => l.Line.Id))
        {
            if (group.Sum(l => (long)l.Input.Quantity) > group.First().Line.RemainingQuantity)
                foreach (var row in group)
                    errors.Add(PurchaseOrderReceiptErrors.ForLine(Errors.PurchaseOrder.ReceiptQuantityOverRemaining, row.Input.ClientLineId, row.Index, "quantity"));
        }
        foreach (var group in resolvedLines.GroupBy(l => (l.Line.ItemId, BatchNumber: l.Input.BatchNumber.Trim())).Where(g => g.Count() > 1))
            foreach (var row in group)
                errors.Add(PurchaseOrderReceiptErrors.ForLine(Errors.Inventory.BatchNumberAlreadyExists, row.Input.ClientLineId, row.Index, "batchNumber"));

        var items = await itemRepository.GetByIdsAsync(command.ActiveShopId, resolvedLines.Select(l => l.Line.ItemId).Distinct().ToList(), cancellationToken);
        foreach (var row in resolvedLines)
        {
            var item = items.SingleOrDefault(i => i.Id == row.Line.ItemId && i.ShopId == command.ActiveShopId);
            if (item is null)
            {
                errors.Add(PurchaseOrderReceiptErrors.ForLine(Errors.PurchaseOrder.LineItemNotFound, row.Input.ClientLineId, row.Index, "purchaseOrderLineId"));
                continue;
            }
            var quantity = QuantityPolicy.Validate(row.Input.Quantity, item.QuantityScale, item.Uom);
            if (quantity.IsError)
                errors.Add(PurchaseOrderReceiptErrors.ForLine(Errors.Inventory.FromQuantityCheck(quantity.FirstError, item.Uom, item.QuantityScale), row.Input.ClientLineId, row.Index, "quantity"));
        }
        if (errors.Count > 0) return errors;

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

        foreach (var (lineInput, poLine, index) in resolvedLines)
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
                return inboundResult.Errors.Select(error => PurchaseOrderReceiptErrors.ForLine(
                    error, lineInput.ClientLineId, index,
                    error.Code == Errors.Inventory.BatchNumberAlreadyExists.Code ? "batchNumber" : "purchaseOrderLineId")).ToList();
            }

            var receiptLine = receipt.AddLine(
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
                lineInput.PurchaseTaxIncluded,
                lineInput.ClientLineId);

            inboundResult.Value.ReceiptValue?.LinkPurchaseOrderReceiptLine(receiptLine.Id);

            po.ApplyReceipt(poLine.Id, lineInput.Quantity);
        }
        po.AddReceipt(receipt);

        await receiptRepository.AddAsync(receipt, cancellationToken);
        purchaseOrderRepository.Update(po);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PurchaseOrderDtoMapper.ToDetail(po);
    }
}
