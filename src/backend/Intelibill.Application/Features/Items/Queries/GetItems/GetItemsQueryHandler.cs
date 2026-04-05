using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Queries.GetItems;

public sealed class GetItemsQueryHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository)
{
    public async Task<ErrorOr<IReadOnlyList<ItemDto>>> HandleAsync(GetItemsQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        var items = await itemRepository.GetByShopIdAsync(query.ActiveShopId, cancellationToken);

        return items
            .Select(item => new ItemDto(
                item.Id,
                item.Name,
                item.Barcode,
                item.Description,
                item.Uom,
                item.IsActive))
            .ToList();
    }
}
