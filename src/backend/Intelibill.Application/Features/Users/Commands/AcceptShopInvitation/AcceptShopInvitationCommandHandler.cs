using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.AcceptShopInvitation;

public sealed class AcceptShopInvitationCommandHandler(
    IMembershipAuthorityRepository authority,
    TimeProvider clock)
{
    public async Task<ErrorOr<MembershipDto>> HandleAsync(
        AcceptShopInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await using var transaction = await authority.BeginAsync(cancellationToken);

        // Unlocked read first, only to learn which accounts and shop the lock protocol has to
        // cover. Every decision below is made against the locked re-read.
        var offer = await authority.PeekInvitationAsync(command.InvitationId, cancellationToken);
        if (offer is null)
            return Errors.Users.InvitationNotFound;

        await authority.LockUsersAsync([command.ActorUserId, offer.InvitedByUserId], cancellationToken);
        if (!await authority.LockShopAsync(offer.ShopId, cancellationToken))
            return Errors.Shop.ShopNotFound;

        var actor = await authority.FindUserAsync(command.ActorUserId, cancellationToken);
        if (actor is null || !actor.IsLoginEnabled)
            return Errors.Auth.UserNotFound;

        // The invitation row is the serialization point for concurrent redemptions: the second
        // transaction waits here and then sees it consumed.
        var invitation = await authority.LockInvitationAsync(command.InvitationId, cancellationToken);
        if (invitation is null)
            return Errors.Users.InvitationNotFound;

        // Ownership of the account comes before ownership of the offer: the token alone proves
        // nothing without the verified address the offer was sent to.
        if (actor.Email is null || !actor.IsEmailVerified)
            return Errors.Users.InvitationRequiresVerifiedEmail;

        if (!string.Equals(actor.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
            return Errors.Users.InvitationWrongAccount;

        if (!invitation.IsPending(now) || !InvitationTokens.Matches(invitation.TokenHash, command.Token))
            return Errors.Users.InvitationNotAcceptable;

        // An invitation is only as live as the authority that issued it. If the inviter has lost
        // ownership of the shop, or their membership has moved on since the offer was made, the
        // grant is stale rather than honoured.
        var inviter = await authority.LockMembershipAsync(invitation.ShopId, invitation.InvitedByUserId, cancellationToken);
        if (inviter is null
            || !inviter.IsActive
            || inviter.Role != ShopRole.Owner
            || inviter.AuthorityVersion != invitation.InviterAuthorityVersion)
            return Errors.Users.InvitationNotAcceptable;

        var membership = await authority.LockMembershipAsync(invitation.ShopId, command.ActorUserId, cancellationToken);
        if (membership is null)
        {
            membership = ShopMembership.Create(invitation.ShopId, command.ActorUserId, invitation.Role, isDefault: false);
            // Added through the account as well, so the operator-recovery marker that protects a
            // member's business access is set where it always is.
            actor.AddShopMembership(membership);
            await authority.AddMembershipAsync(membership, cancellationToken);
        }
        else if (!membership.IsActive)
        {
            membership.Reinstate(invitation.Role);
        }

        // Already-active members get an idempotent result: the offer is consumed, the membership
        // is untouched, and no second row appears.
        invitation.Accept(command.ActorUserId, now);

        if (!await authority.CommitAsync(cancellationToken))
            return Errors.Users.InvitationNotAcceptable;

        return new MembershipDto(
            membership.UserId,
            membership.ShopId,
            $"{actor.FirstName} {actor.LastName}".Trim(),
            MembershipAuthority.ToRoleLabel(membership.Role),
            membership.Status.ToString(),
            membership.AuthorityVersion);
    }
}
