using System;
using System.Collections.Generic;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

public sealed record UpdatePurchaseOrderLineInput(
    string Description,
    int ExpectedQuantity,
    decimal UnitCost);

public sealed record UpdatePurchaseOrderDraftCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId,
    string? Notes,
    IReadOnlyList<UpdatePurchaseOrderLineInput> Lines);
