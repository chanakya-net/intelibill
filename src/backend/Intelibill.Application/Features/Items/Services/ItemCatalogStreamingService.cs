using System.Runtime.CompilerServices;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Services;

public sealed class ItemCatalogStreamingService(
    IUserRepository userRepository,
    IItemRepository itemRepository) : IItemCatalogStreamingService
{
    public async Task<ErrorOr<Success>> ValidateAccessAsync(
        Guid userId,
        Guid activeShopId,
        CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(userId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var isMember = caller.ShopMemberships.Any(sm => sm.ShopId == activeShopId);
        if (!isMember)
            return Errors.Shop.MembershipNotFound;

        return Result.Success;
    }

    public async IAsyncEnumerable<ItemCatalogEntryDto> StreamByShopAsync(
        Guid activeShopId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in itemRepository.StreamByShopIdAsync(activeShopId, cancellationToken))
        {
            yield return new ItemCatalogEntryDto(item.Id, item.Name, item.Barcode);
        }
    }

}
