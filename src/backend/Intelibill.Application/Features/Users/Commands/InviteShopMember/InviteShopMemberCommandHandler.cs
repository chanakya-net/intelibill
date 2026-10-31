using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.InviteShopMember;

public sealed class InviteShopMemberCommandHandler(
    IMembershipAuthorityRepository authority,
    IShopRepository shopRepository,
    IIdentityMailOutbox mail,
    TimeProvider clock)
{
    private const int InvitationLifetimeDays = 7;

    /// <summary>
    /// A coarse per-shop ceiling, so a compromised owner session cannot use the shop as a mail
    /// relay. Per-destination budgets belong to plan 08's abuse controls.
    /// </summary>
    private const int InvitationsPerShopPerHour = 20;

    public async Task<ErrorOr<ShopInvitationDto>> HandleAsync(
        InviteShopMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (!MembershipAuthority.TryParseAssignableRole(command.Role, out var role))
            return Errors.Users.RoleNotSupported;

        if (!mail.Enabled)
            return Error.Failure(
                "Users.InvitationDeliveryUnavailable",
                "Email delivery is temporarily unavailable, so invitations cannot be sent.");

        var email = command.Email.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow();

        await using var transaction = await authority.BeginAsync(cancellationToken);

        await authority.LockUsersAsync([command.ActorUserId], cancellationToken);
        if (!await authority.LockShopAsync(command.ShopId, cancellationToken))
            return Errors.Shop.ShopNotFound;

        var actorMembership = await authority.LockMembershipAsync(command.ShopId, command.ActorUserId, cancellationToken);
        if (actorMembership is null || !actorMembership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var shop = await shopRepository.GetByIdWithMembersAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        // An address with live access needs no invitation. A suspended or removed membership does:
        // accepting a fresh invitation is how it is reinstated.
        var invited = shop.Memberships.FirstOrDefault(sm =>
            sm.User is not null && string.Equals(sm.User.Email, email, StringComparison.OrdinalIgnoreCase));
        if (invited is { IsActive: true })
            return Errors.Users.InvitationAlreadyMember;

        if (await authority.CountRecentInvitationsAsync(command.ShopId, now.AddHours(-1), cancellationToken)
            >= InvitationsPerShopPerHour)
            return Errors.Users.InvitationBudgetExhausted;

        // A resend supersedes: at most one link for this shop and address is ever redeemable.
        await authority.RevokePendingInvitationsAsync(command.ShopId, email, now, cancellationToken);

        var token = InvitationTokens.New();
        var invitation = ShopInvitation.Create(
            command.ShopId,
            email,
            role,
            InvitationTokens.Hash(token),
            command.ActorUserId,
            actorMembership.AuthorityVersion,
            now.AddDays(InvitationLifetimeDays));

        authority.AddInvitation(invitation);
        mail.QueueShopInvitation(
            invitation.Id,
            email,
            shop.Name,
            MembershipAuthority.ToRoleLabel(role),
            token,
            invitation.ExpiresAt);

        if (!await authority.CommitAsync(cancellationToken))
            return Errors.Users.InvitationAlreadyMember;

        return new ShopInvitationDto(
            invitation.Id,
            invitation.ShopId,
            invitation.Email,
            MembershipAuthority.ToRoleLabel(invitation.Role),
            invitation.DescribeStatus(now),
            invitation.ExpiresAt,
            invitation.CreatedAt);
    }
}
