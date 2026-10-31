using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Queries.GetShopInvitations;

public sealed class GetShopInvitationsQueryHandler(
    IMembershipAuthorityRepository authority,
    IShopRepository shopRepository,
    TimeProvider clock)
{
    public async Task<ErrorOr<IReadOnlyList<ShopInvitationDto>>> HandleAsync(
        GetShopInvitationsQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await shopRepository.GetMembershipAsync(query.ActorUserId, query.ShopId, cancellationToken);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var now = clock.GetUtcNow();
        var invitations = await authority.GetInvitationsForShopAsync(query.ShopId, cancellationToken);

        return invitations
            .Select(x => new ShopInvitationDto(
                x.Id,
                x.ShopId,
                x.Email,
                MembershipAuthority.ToRoleLabel(x.Role),
                x.DescribeStatus(now),
                x.ExpiresAt,
                x.CreatedAt))
            .ToList();
    }
}
