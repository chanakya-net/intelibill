using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Expiry;
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
    IPurchaseOrderRepository purchaseOrderRepository,
    IPurchaseOrderReceiptRepository receiptRepository,
    IInboundInventoryLineProcessor inboundInventoryLineProcessor,
    IPurchaseOrderReceiptNumberGenerator receiptNumberGenerator,
    IUnitOfWork unitOfWork,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    IItemRepository itemRepository,
    IFinancialOperationExecutor operationExecutor,
    IExpiryPolicyFence expiryFence,
    StockIntakeAccess access)
{
    [LicenceAction(LicenceAction.NewTrading)]
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        ReceivePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        var roleRefusal = Errors.PurchaseOrder.UserCannotMutatePurchaseOrder;
        Task<Error?> CheckAuthorityAsync(CancellationToken ct) =>
            access.CheckAuthorityAsync(command.ActorUserId, command.ActiveShopId, command.Session, ct, roleRefusal);

        if (await CheckAuthorityAsync(cancellationToken) is { } denied)
            return denied;

        ShopLicenceLease lease = default;
        Task<Error?> CheckWriteAsync(CancellationToken ct) =>
            access.CheckWriteAsync(command.ActorUserId, command.ActiveShopId, command.Session, lease, ct, roleRefusal);

        async Task<ErrorOr<PurchaseOrderDetailDto>> ReceiveAsync(CancellationToken ct)
        {
            var admission = await access.EnterAsync(command.ActorUserId, command.ActiveShopId, command.Session, ct, roleRefusal);
            if (admission.IsError) return admission.Errors;
            lease = admission.Value;

            var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, ct);
            if (shop is null) return Errors.Shop.ShopNotFound;

            var receivedAt = (command.ReceivedAt ?? businessClock.UtcNow).ToUniversalTime();
            var result = await ReceiveCoreAsync(command, receivedAt, shop, CheckWriteAsync, ct);
            if (!result.IsError && await CheckWriteAsync(ct) is { } afterSave)
                return afterSave;
            return result;
        }

        if (command.OperationId is not Guid operationId)
            return await PostingTransaction.RunAsync(unitOfWork, ReceiveAsync, cancellationToken);

        var scope = new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.PurchaseOrderReceipt, operationId.ToString("D"))
        {
            RecheckBeforeCommit = CheckWriteAsync,
            RecheckReplayAuthority = CheckAuthorityAsync,
        };
        return await operationExecutor.ExecuteAsync(scope, command.ActorUserId, command.PurchaseOrderId,
            FinancialOperationHasher.Compute(PurchaseOrderReceiptPayload.From(command)), ReceiveAsync, cancellationToken);
    }

    private async Task<ErrorOr<PurchaseOrderDetailDto>> ReceiveCoreAsync(
        ReceivePurchaseOrderCommand command, DateTimeOffset receivedAt, Shop shop,
        Func<CancellationToken, Task<Error?>> checkWrite, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(command, cancellationToken) is { } errors) return errors;

        // The period fence precedes the order and line row locks.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ActiveShopId,
            businessClock.GetBusinessDate(receivedAt, shop.BusinessTimeZoneId), cancellationToken);
        if (fence.IsError) return fence.Errors;

        var expiryLock = await expiryFence.AcquireAsync(command.ActiveShopId, false, cancellationToken);
        if (expiryLock.IsError) return expiryLock.Errors;
        var po = await purchaseOrderRepository.GetReceiptDetailForUpdateAsync(command.ActiveShopId, command.PurchaseOrderId, cancellationToken);
        if (await checkWrite(cancellationToken) is { } afterLocks) return afterLocks;
        if (po is null) return Errors.PurchaseOrder.NotFound;
        if (po.Status is not (PurchaseOrderStatus.Placed or PurchaseOrderStatus.PartiallyReceived))
            return Errors.PurchaseOrder.CannotReceiveInvalidStatus;

        var lines = await ResolveLinesAsync(command, po, cancellationToken);
        if (lines.IsError) return lines.Errors;
        return await PostReceiptAsync(command, receivedAt, po, lines.Value, checkWrite, cancellationToken);
    }

    private static async Task<List<Error>?> ValidateAsync(ReceivePurchaseOrderCommand command, CancellationToken cancellationToken)
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

        return null;
    }

    private async Task<ErrorOr<List<ResolvedLine>>> ResolveLinesAsync(
        ReceivePurchaseOrderCommand command, PurchaseOrder po, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        var resolvedLines = new List<ResolvedLine>();
        for (var index = 0; index < command.Lines.Count; index++)
        {
            var input = command.Lines[index];
            var line = po.Lines.FirstOrDefault(l => l.Id == input.PurchaseOrderLineId);
            if (line is null)
                errors.Add(PurchaseOrderReceiptErrors.ForLine(Errors.PurchaseOrder.ReceiptLineNotFound, input.ClientLineId, index, "purchaseOrderLineId"));
            else
                resolvedLines.Add(new(input, line, index));
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
        return errors.Count > 0 ? errors : resolvedLines;
    }

    private async Task<ErrorOr<PurchaseOrderDetailDto>> PostReceiptAsync(
        ReceivePurchaseOrderCommand command, DateTimeOffset receivedAt, PurchaseOrder po,
        List<ResolvedLine> resolvedLines, Func<CancellationToken, Task<Error?>> checkWrite, CancellationToken cancellationToken)
    {
        if (await checkWrite(cancellationToken) is { } beforeAllocation) return beforeAllocation;
        var receiptNumber = await receiptNumberGenerator.GenerateAsync(command.ActiveShopId, receivedAt.Year, cancellationToken);
        if (await checkWrite(cancellationToken) is { } afterAllocation) return afterAllocation;
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

        foreach (var row in resolvedLines)
        {
            var posted = await StageLineAsync(command, po, receipt, row,
                receivedAt, itemResolutionContext, inventoryUpdateContext, cancellationToken);
            if (posted.IsError) return posted.Errors;
            po.ApplyReceipt(row.Line.Id, row.Input.Quantity);
        }
        po.AddReceipt(receipt);

        await receiptRepository.AddAsync(receipt, cancellationToken);
        purchaseOrderRepository.Update(po);
        if (await checkWrite(cancellationToken) is { } beforeSave) return beforeSave;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PurchaseOrderDtoMapper.ToDetail(po);
    }

    private async Task<ErrorOr<Success>> StageLineAsync(
        ReceivePurchaseOrderCommand command, PurchaseOrder po, PurchaseOrderReceipt receipt,
        ResolvedLine row, DateTimeOffset receivedAt, ItemResolutionContext itemResolutionContext,
        InventoryUpdateContext inventoryUpdateContext, CancellationToken cancellationToken)
    {
        var (lineInput, poLine, index) = row;
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

        return Result.Success;
    }

    private sealed record ResolvedLine(ReceivePurchaseOrderLineInput Input, PurchaseOrderLine Line, int Index);
}
