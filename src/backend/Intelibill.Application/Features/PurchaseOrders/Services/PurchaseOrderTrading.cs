using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;

namespace Intelibill.Application.Features.PurchaseOrders.Services;

public sealed class PurchaseOrderTrading(StockIntakeAccess access, IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> RunAsync(
        Guid actor, Guid shop, SessionClaims? session, Error roleRefusal,
        Func<CancellationToken, Task<ErrorOr<PurchaseOrder>>> stage, CancellationToken ct)
    {
        if (await access.CheckAuthorityAsync(actor, shop, session, ct, roleRefusal) is { } denied)
            return denied;

        return await PostingTransaction.RunAsync<PurchaseOrderDetailDto>(unitOfWork, async token =>
        {
            var lease = await access.EnterAsync(actor, shop, session, token, roleRefusal);
            if (lease.IsError) return lease.Errors;

            var order = await stage(token);
            if (order.IsError) return order.Errors;
            if (await access.CheckWriteAsync(actor, shop, session, lease.Value, token, roleRefusal) is { } beforeSave)
                return beforeSave;

            await unitOfWork.SaveChangesAsync(token);
            if (await access.CheckWriteAsync(actor, shop, session, lease.Value, token, roleRefusal) is { } afterSave)
                return afterSave;

            return PurchaseOrderDtoMapper.ToDetail(order.Value);
        }, ct);
    }
}
