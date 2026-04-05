using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Queries.StreamItems;

public sealed class StreamItemsQueryHandler(
    IUserRepository userRepository)
{
    public async Task<ErrorOr<Success>> HandleAsync(StreamItemsQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var isMember = caller.ShopMemberships.Any(sm => sm.ShopId == query.ActiveShopId);
        if (!isMember)
            return Errors.Shop.MembershipNotFound;

        return Result.Success;
    }
}
