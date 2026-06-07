using System;
using System.Collections.Generic;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

public sealed record UpdatePurchaseOrderLineInput(
    Guid ItemId,
    string Description,
    int ExpectedQuantity,
    decimal UnitCost);

public sealed record UpdatePurchaseOrderDraftCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId,
    Guid? SupplierId,
    DateOnly? OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string? SupplierReferenceNumber,
    string? Notes,
    IReadOnlyList<UpdatePurchaseOrderLineInput> Lines);
