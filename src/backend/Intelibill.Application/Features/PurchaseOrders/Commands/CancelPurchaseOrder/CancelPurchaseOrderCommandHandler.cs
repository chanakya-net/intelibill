using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

public sealed class CancelPurchaseOrderCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        CancelPurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner && membership.Role != ShopRole.Manager)
            return Errors.PurchaseOrder.UserCannotMutatePurchaseOrder;

        return await PostingTransaction.RunAsync<PurchaseOrderDetailDto>(unitOfWork,
            ct => CompleteAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<PurchaseOrderDetailDto>> CompleteAsync(CancelPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var licence = await licenceFence.EnterForSessionAsync(authority, command.Session, command.ActorUserId,
            command.ActiveShopId, LicenceAction.SourceCorrection, cancellationToken);
        if (licence.IsError) return licence.Errors;
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;

        var po = await purchaseOrderRepository.GetReceiptDetailForUpdateAsync(
            command.ActiveShopId,
            command.PurchaseOrderId,
            cancellationToken);

        if (po is null)
            return Errors.PurchaseOrder.NotFound;

        if (licence.Value.CurrentDecision().ExistingSourceDenial(po.CreatedAt) is { } sourceDenial)
            return sourceDenial;

        if (po.Status != PurchaseOrderStatus.Placed)
            return Errors.PurchaseOrder.CannotCancelInvalidStatus;

        if (string.IsNullOrWhiteSpace(command.Reason))
            return Errors.PurchaseOrder.CancellationReasonRequired;

        if (po.Lines.Sum(l => l.ReceivedQuantity) > 0)
            return Errors.PurchaseOrder.CannotCancelAfterReceipt;

        po.Cancel(command.Reason);

        purchaseOrderRepository.Update(po);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PurchaseOrderDtoMapper.ToDetail(po);
    }
}
