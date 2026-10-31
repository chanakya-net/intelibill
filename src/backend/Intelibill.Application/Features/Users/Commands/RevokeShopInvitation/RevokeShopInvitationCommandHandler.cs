using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.RevokeShopInvitation;

public sealed class RevokeShopInvitationCommandHandler(
    IMembershipAuthorityRepository authority,
    TimeProvider clock)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RevokeShopInvitationCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await authority.BeginAsync(cancellationToken);

        await authority.LockUsersAsync([command.ActorUserId], cancellationToken);
        if (!await authority.LockShopAsync(command.ShopId, cancellationToken))
            return Errors.Shop.ShopNotFound;

        var actorMembership = await authority.LockMembershipAsync(command.ShopId, command.ActorUserId, cancellationToken);
        if (actorMembership is null || !actorMembership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var invitation = await authority.LockInvitationAsync(command.InvitationId, cancellationToken);
        if (invitation is null || invitation.ShopId != command.ShopId)
            return Errors.Users.InvitationNotFound;

        var now = clock.GetUtcNow();
        if (!invitation.IsPending(now))
            return Errors.Users.InvitationNotAcceptable;

        invitation.Revoke(now);

        return await authority.CommitAsync(cancellationToken)
            ? true
            : Errors.Users.InvitationNotAcceptable;
    }
}
