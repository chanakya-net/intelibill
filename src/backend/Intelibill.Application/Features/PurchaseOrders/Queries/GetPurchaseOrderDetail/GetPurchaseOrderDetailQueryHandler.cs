using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;

public sealed record GetPurchaseOrderDetailQuery(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId);

public sealed class GetPurchaseOrderDetailQueryHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    ISupplierReturnRepository? supplierReturns = null,
    IPurchaseReceiptReversalRepository? receiptReversals = null)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        GetPurchaseOrderDetailQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(query.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var po = await purchaseOrderRepository.GetByShopAndIdAsync(
            query.ActiveShopId,
            query.PurchaseOrderId,
            cancellationToken);
        if (po is null)
            return Errors.PurchaseOrder.NotFound;

        var dto=PurchaseOrderDtoMapper.ToDetail(po);
        if(supplierReturns is null || receiptReversals is null) return dto;
        var receiptRows=await supplierReturns.GetByPoLineIdsAsync(query.ActiveShopId,po.Receipts.SelectMany(r=>r.Lines).Select(x=>x.Id).ToArray(),cancellationToken);
        var ids=receiptRows.Select(x=>x.Id).ToArray();
        var shipments=await supplierReturns.GetByReceiptsAsync(query.ActiveShopId,ids,cancellationToken);
        var reversed=await receiptReversals.GetByReceiptsAsync(query.ActiveShopId,ids,cancellationToken);
        var receiptDtos=dto.Receipts!.Select(r=>r with { Lines=r.Lines.Select(line=>
        {
            var source=receiptRows.SingleOrDefault(x=>x.PurchaseOrderReceiptLineId==line.ReceiptLineId);
            if(source is null) return line;
            var shipped=shipments.SelectMany(x=>x.Lines).Where(x=>x.ReceiptValueId==source.Id).Sum(x=>x.Quantity);
            var reversedQuantity=reversed.Where(x=>x.ReceiptValueId==source.Id).Sum(x=>x.Quantity);
            var batch=po.Receipts.SelectMany(x=>x.Lines).Single(x=>x.Id==line.ReceiptLineId).InventoryBatch;
            return line with{ReceiptValueId=source.Id,ReceiptVersion=source.Version,BatchVersion=batch?.Version??0,ReturnedQuantity=shipped,ReversedQuantity=reversedQuantity,NetRetainedQuantity=line.Quantity-shipped-reversedQuantity};
        }).ToArray()}).ToArray();
        var lineDtos=dto.Lines.Select(line=>
        {
            var lots=receiptDtos.SelectMany(x=>x.Lines).Where(x=>x.PurchaseOrderLineId==line.LineId).ToArray();
            var shipped=lots.Sum(x=>x.ReturnedQuantity); var reversedQuantity=lots.Sum(x=>x.ReversedQuantity);
            return line with{ReturnedQuantity=shipped,ReversedQuantity=reversedQuantity,NetRetainedQuantity=line.ReceivedQuantity-shipped-reversedQuantity};
        }).ToArray();
        return dto with{Receipts=receiptDtos,Lines=lineDtos,ReturnedQuantity=lineDtos.Sum(x=>x.ReturnedQuantity),ReversedQuantity=lineDtos.Sum(x=>x.ReversedQuantity),NetRetainedQuantity=lineDtos.Sum(x=>x.NetRetainedQuantity??x.ReceivedQuantity)};
    }
}
