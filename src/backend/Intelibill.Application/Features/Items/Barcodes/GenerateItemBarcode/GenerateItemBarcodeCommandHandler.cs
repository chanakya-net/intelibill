using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Barcodes.GenerateItemBarcode;

public sealed class GenerateItemBarcodeCommandHandler(
    IUserRepository userRepository,
    IItemBarcodeSequenceRepository itemBarcodeSequenceRepository,
    IItemRepository itemRepository,
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    private const int MaxCollisionRetries = 10;

    [LicenceAction(LicenceAction.OperationalCapacity)]
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

        var result = await PostingTransaction.RunAsync<Allocation>(unitOfWork,
            ct => AllocateAsync(command, ct), cancellationToken);
        if (result.IsError) return result.Errors;
        return result.Value.Code is { } code
            ? new GenerateItemBarcodeResultDto(code)
            : Errors.Item.BarcodeGenerationFailed;
    }

    private async Task<ErrorOr<Allocation>> AllocateAsync(GenerateItemBarcodeCommand command, CancellationToken ct)
    {
        var shopId = command.ActiveShopId;
        var lease = await licenceFence.EnterForSessionAsync(authority, command.Session, command.ActorUserId,
            shopId, LicenceAction.OperationalCapacity, ct);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;

        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var nextCode = await itemBarcodeSequenceRepository.GetNextCodeAsync(shopId, ct);
            if (lease.Value.CurrentDecision().EnforcedDenial() is { } currentDenial) return currentDenial;

            if (await itemRepository.GetByBarcodeAsync(shopId, nextCode, ct) is null)
                return new Allocation(nextCode);
        }

        // The sequence has advanced past the colliding codes even when none was available to return.
        return new Allocation(null);
    }

    private sealed record Allocation(string? Code);
}
