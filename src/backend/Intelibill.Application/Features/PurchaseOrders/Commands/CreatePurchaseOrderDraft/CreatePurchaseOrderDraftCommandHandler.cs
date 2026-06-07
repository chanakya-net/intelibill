using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
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
    IReadOnlyList<CreatePurchaseOrderLineInput> Lines);

public sealed class CreatePurchaseOrderDraftCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IPurchaseOrderNumberGenerator numberGenerator,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        CreatePurchaseOrderDraftCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner && membership.Role != ShopRole.Manager)
            return Errors.PurchaseOrder.UserCannotCreatePurchaseOrder;

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var poNumber = await numberGenerator.GenerateAsync(command.ActiveShopId, now.Year, cancellationToken);

            var po = PurchaseOrder.CreateDraft(
                command.ActiveShopId,
                poNumber,
                command.SupplierId,
                command.OrderDate,
                command.ExpectedDeliveryDate,
                command.SupplierReferenceNumber,
                command.Notes);

            var itemIds = new HashSet<Guid>();
            foreach (var lineInput in command.Lines)
            {
                if (lineInput.ItemId == Guid.Empty)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.LineDescriptionRequired;
                }

                var description = lineInput.Description?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(description))
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.LineDescriptionRequired;
                }

                if (!itemIds.Add(lineInput.ItemId))
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.DuplicateItem;
                }

                if (lineInput.ExpectedQuantity <= 0)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.InvalidLineQuantity;
                }

                if (lineInput.UnitCost < 0)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.InvalidLineUnitCost;
                }

                po.AddLine(lineInput.ItemId, description, lineInput.ExpectedQuantity, lineInput.UnitCost);
            }

            await purchaseOrderRepository.AddAsync(po, cancellationToken);
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
