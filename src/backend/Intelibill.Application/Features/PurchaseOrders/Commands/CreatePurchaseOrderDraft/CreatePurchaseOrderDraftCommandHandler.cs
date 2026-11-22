using Intelibill.Application.Common.Security;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;

public sealed record CreatePurchaseOrderLineInput(
    Guid ItemId,
    string Description,
    int ExpectedQuantity,
    decimal UnitCost);

public sealed record CreatePurchaseOrderDraftCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid? SupplierId,
    DateOnly? OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string? SupplierReferenceNumber,
    string? Notes,
    string? SupplierName,
    string? SupplierReference,
    IReadOnlyList<CreatePurchaseOrderLineInput> Lines,
    SessionClaims? Session = null);

public sealed class CreatePurchaseOrderDraftCommandHandler(
    IItemRepository itemRepository,
    ISupplierRepository supplierRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IPurchaseOrderNumberGenerator numberGenerator,
    PurchaseOrderTrading trading)
{
    [LicenceAction(LicenceAction.NewTrading)]
    public Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        CreatePurchaseOrderDraftCommand command, CancellationToken cancellationToken) =>
        trading.RunAsync(command.ActorUserId, command.ActiveShopId, command.Session,
            Errors.PurchaseOrder.UserCannotCreatePurchaseOrder, ct => StageAsync(command, ct), cancellationToken);

    private async Task<ErrorOr<PurchaseOrder>> StageAsync(
        CreatePurchaseOrderDraftCommand command, CancellationToken cancellationToken)
    {
        Supplier? supplier = null;
        if (command.SupplierId is Guid supplierId)
        {
            supplier = await supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (supplier is null || supplier.ShopId != command.ActiveShopId)
                return Errors.Supplier.SupplierNotFound;
        }

        var now = DateTimeOffset.UtcNow;
        var poNumber = await numberGenerator.GenerateAsync(command.ActiveShopId, now.Year, cancellationToken);

        var po = PurchaseOrder.CreateDraft(
            command.ActiveShopId,
            poNumber,
            command.SupplierId,
            command.OrderDate,
            command.ExpectedDeliveryDate,
            command.SupplierReferenceNumber,
            command.Notes,
            supplier?.Name ?? command.SupplierName,
            command.SupplierReference);

        var lines = await PurchaseOrderDraftLines.ValidateAsync(command.ActiveShopId,
            command.Lines.Select(line => (line.ItemId, line.Description, line.ExpectedQuantity, line.UnitCost)),
            itemRepository, cancellationToken);
        if (lines.IsError) return lines.Errors;

        foreach (var line in lines.Value)
            po.AddLine(line.ItemId, line.Description, line.ExpectedQuantity, line.UnitCost);

        await purchaseOrderRepository.AddAsync(po, cancellationToken);
        return po;
    }
}
