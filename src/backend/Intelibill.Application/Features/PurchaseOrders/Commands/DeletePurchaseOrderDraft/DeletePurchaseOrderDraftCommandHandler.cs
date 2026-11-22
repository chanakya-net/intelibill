using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.DeletePurchaseOrderDraft;

public sealed class DeletePurchaseOrderDraftCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<Deleted>> HandleAsync(
        DeletePurchaseOrderDraftCommand command,
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

        return await PostingTransaction.RunAsync<Deleted>(unitOfWork,
            ct => CompleteAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<Deleted>> CompleteAsync(DeletePurchaseOrderDraftCommand command, CancellationToken cancellationToken)
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

        if (!po.CanDeleteDraft)
            return Errors.PurchaseOrder.CannotDeleteNonDraft;

        purchaseOrderRepository.Remove(po);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}
