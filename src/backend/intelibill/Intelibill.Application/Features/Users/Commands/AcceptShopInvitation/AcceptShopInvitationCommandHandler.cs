using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.AcceptShopInvitation;

public sealed class AcceptShopInvitationCommandHandler(
    IMembershipAuthorityRepository authority,
    TimeProvider clock,
    ITenantWriteScope tenantScope,
    IShopLicenceFence fence,
    ILiveSessionAuthority sessions)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
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

        // A valid invitation identifies the target independently of the device's selected shop.
        if (!offer.IsPending(now) || !InvitationTokens.Matches(offer.TokenHash, command.Token))
            return Errors.Users.InvitationNotAcceptable;
        await tenantScope.EnterAsync(offer.ShopId, cancellationToken);
        var licence = await fence.EnterAsync(offer.ShopId, LicenceAction.OperationalCapacity, cancellationToken);

        await authority.LockUsersAsync([command.ActorUserId, offer.InvitedByUserId], cancellationToken);
        // Revocation takes the same account lock, so a device signed out while this request queued
        // is refused here, before any seat detail or write.
        if (!await IsLiveAsync(command, cancellationToken))
            return Errors.Auth.SessionInvalidated;
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

        if (InvitationRefusal(actor, invitation, command, now) is { } refusal)
            return refusal;

        // An invitation is only as live as the authority that issued it. If the inviter has lost
        // ownership of the shop, or their membership has moved on since the offer was made, the
        // grant is stale rather than honoured.
        var inviter = await authority.LockMembershipAsync(invitation.ShopId, invitation.InvitedByUserId, cancellationToken);
        if (!IsInviterAuthorityCurrent(inviter, invitation))
            return Errors.Users.InvitationNotAcceptable;

        if (licence.IsError) return licence.Errors;
        if (licence.Value.SuspensionDenial() is { } denied) return denied;

        var membership = await authority.LockMembershipAsync(invitation.ShopId, command.ActorUserId, cancellationToken);

        // Every authority lock is now held. The device session is judged again at this instant: one
        // that lapsed while queued behind the shop root neither seats anyone nor learns the shop is full.
        if (!await IsLiveAsync(command, cancellationToken))
            return Errors.Auth.SessionInvalidated;

        // The offer's deadline is judged when every lock is held, not when the request arrived:
        // an offer that lapsed while queued neither seats anyone nor reports a full shop.
        var decidedAt = clock.GetUtcNow();
        if (!invitation.IsPending(decidedAt))
            return Errors.Users.InvitationNotAcceptable;

        // Joining or rejoining takes a seat; the offer stays pending when none is free.
        if (membership is not { IsActive: true }
            && await SeatDenialAsync(licence.Value, invitation.ShopId, cancellationToken) is { } full)
            return full;

        membership = await SeatMemberAsync(command, actor, invitation, membership, cancellationToken);

        // Already-active members get an idempotent result: the offer is consumed, the membership
        // is untouched, and no second row appears.
        invitation.Accept(command.ActorUserId, decidedAt);

        if (!await authority.CommitAsync(cancellationToken))
            return Errors.Users.InvitationNotAcceptable;

        return ToDto(actor, membership);
    }

    private static Error? InvitationRefusal(
        User actor,
        ShopInvitation invitation,
        AcceptShopInvitationCommand command,
        DateTimeOffset now)
    {
        // Ownership of the account comes before ownership of the offer: the token alone proves
        // nothing without the verified address the offer was sent to.
        if (actor.Email is null || !actor.IsEmailVerified)
            return Errors.Users.InvitationRequiresVerifiedEmail;

        if (!string.Equals(actor.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
            return Errors.Users.InvitationWrongAccount;

        if (!invitation.IsPending(now) || !InvitationTokens.Matches(invitation.TokenHash, command.Token))
            return Errors.Users.InvitationNotAcceptable;

        return null;
    }

    private static bool IsInviterAuthorityCurrent(ShopMembership? inviter, ShopInvitation invitation) =>
        inviter is not null
        && inviter.IsActive
        && inviter.Role == ShopRole.Owner
        && inviter.AuthorityVersion == invitation.InviterAuthorityVersion;

    private async Task<ShopMembership> SeatMemberAsync(
        AcceptShopInvitationCommand command,
        User actor,
        ShopInvitation invitation,
        ShopMembership? membership,
        CancellationToken cancellationToken)
    {
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

        return membership;
    }

    private static MembershipDto ToDto(User actor, ShopMembership membership) =>
        new(
            membership.UserId,
            membership.ShopId,
            $"{actor.FirstName} {actor.LastName}".Trim(),
            MembershipAuthority.ToRoleLabel(membership.Role),
            membership.Status.ToString(),
            membership.AuthorityVersion);

    private async Task<bool> IsLiveAsync(AcceptShopInvitationCommand command, CancellationToken ct) =>
        command.Session is { } session && session.UserId == command.ActorUserId
        && await sessions.IsValidAsync(session, ct);

    // Counted under the shop root lock taken above, so a concurrent admission cannot share the seat.
    // The invitee is not yet an authority in the shop, so usage figures are withheld.
    private async Task<Error?> SeatDenialAsync(ShopLicenceLease lease, Guid shopId, CancellationToken ct) =>
        MemberSeatPolicy.Applies(lease)
            ? MemberSeatPolicy.AdmissionDenial(lease, await authority.CountActiveMembersAsync(shopId, ct), discloseUsage: false)
            : null;
}
