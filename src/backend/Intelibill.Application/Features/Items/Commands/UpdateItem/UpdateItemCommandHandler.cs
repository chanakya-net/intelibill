using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Wolverine;

namespace Intelibill.Application.Features.Items.Commands.UpdateItem;

public sealed class UpdateItemCommandHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<Success>> HandleAsync(UpdateItemCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Item.UserIsNotOwnerOrManager;

        var result = await PostingTransaction.RunAsync<bool>(unitOfWork,
            ct => UpdateAsync(command, ct), cancellationToken);
        if (result.IsError) return result.Errors;

        if (result.Value)
            await messageBus.PublishAsync(new ItemApplicabilityChangedDomainEvent(command.ActiveShopId, command.ItemId));

        return Result.Success;
    }

    private async Task<ErrorOr<bool>> UpdateAsync(UpdateItemCommand command, CancellationToken cancellationToken)
    {
        var lease = await licenceFence.EnterForSessionAsync(authority, command.Session, command.ActorUserId,
            command.ActiveShopId, LicenceAction.OperationalCapacity, cancellationToken);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.SuspensionDenial() is { } suspension) return suspension;

        var item = await itemRepository.LockForEditAsync(command.ActiveShopId, command.ItemId, cancellationToken);
        if (item is null)
            return Errors.Item.ItemNotFound;

        if (item.ShopId != command.ActiveShopId)
            return Errors.Item.ItemNotFound;

        var normalizedName = command.Name.Trim();
        var normalizedBarcode = command.Barcode.Trim();
        var normalizedUom = command.Uom.Trim();
        var targetIsActive = command.IsActive ?? item.IsActive;
        if (!item.IsActive && targetIsActive && lease.Value.CurrentDecision().EnforcedDenial() is { } denial)
            return denial;

        var isRenamed = normalizedName != item.Name;
        var isDeactivated = item.IsActive && !targetIsActive;

        if (normalizedBarcode != item.Barcode)
        {
            var existingBarcode = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, normalizedBarcode, cancellationToken);
            if (existingBarcode is not null)
                return Errors.Item.BarcodeAlreadyExists;
        }

        var history = item.WouldChangeUnit(normalizedUom, command.QuantityScale)
            ? await itemRepository.GetStockHistoryAsync(command.ActiveShopId, item.Id, cancellationToken)
            : ItemStockHistory.None;

        var unitChange = item.ChangeUnit(normalizedUom, command.QuantityScale, history);
        if (unitChange.IsError)
            return unitChange.Errors;

        item.Update(
            normalizedName,
            command.Description,
            normalizedBarcode,
            targetIsActive,
            command.ActorUserId,
            command.HsnCode,
            command.DefaultTaxRatePercent,
            defaultTaxIncluded: false);

        itemRepository.Update(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return isRenamed || isDeactivated;
    }
}
