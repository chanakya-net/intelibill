using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Barcodes.GenerateItemBarcode;

public sealed class GenerateItemBarcodeCommandHandler(
    IUserRepository userRepository,
    IItemBarcodeSequenceRepository itemBarcodeSequenceRepository,
    IItemRepository itemRepository,
    IUnitOfWork unitOfWork)
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

        ItemBarcodeSequence? sequence;
        try
        {
            sequence = await itemBarcodeSequenceRepository.GetByShopIdAsync(command.ActiveShopId, cancellationToken);
            if (sequence is null)
            {
                sequence = ItemBarcodeSequence.Create(command.ActiveShopId);
                await itemBarcodeSequenceRepository.AddAsync(sequence, cancellationToken);
            }
        }
        catch
        {
            return Errors.Item.BarcodeGenerationFailed;
        }

        if (sequence is null)
            return Errors.Item.BarcodeGenerationFailed;

        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var nextCode = sequence.NextCode();
            var collision = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, nextCode, cancellationToken);
            if (collision is not null)
                continue;

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new GenerateItemBarcodeResultDto(nextCode);
            }
            catch
            {
                return Errors.Item.BarcodeGenerationFailed;
            }
        }

        return Errors.Item.BarcodeGenerationFailed;
    }
}
