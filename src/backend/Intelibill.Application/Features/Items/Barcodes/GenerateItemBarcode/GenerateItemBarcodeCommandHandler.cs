using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Barcodes.GenerateItemBarcode;

public sealed class GenerateItemBarcodeCommandHandler(
    IUserRepository userRepository,
    IItemBarcodeSequenceRepository itemBarcodeSequenceRepository,
    IItemRepository itemRepository)
{
    private const int MaxCollisionRetries = 10;

    public async Task<ErrorOr<GenerateItemBarcodeResultDto>> HandleAsync(
        GenerateItemBarcodeCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(membership => membership.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Item.UserIsNotOwnerOrManager;

        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var nextCode = await itemBarcodeSequenceRepository.GetNextCodeAsync(command.ActiveShopId, cancellationToken);
            var collision = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, nextCode, cancellationToken);
            if (collision is not null)
                continue;

            return new GenerateItemBarcodeResultDto(nextCode);
        }

        return Errors.Item.BarcodeGenerationFailed;
    }
}
