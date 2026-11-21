using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.UpdateShopMembership;

public sealed class UpdateShopMembershipCommandHandler(
    IMembershipAuthorityRepository authority,
    TimeProvider clock,
    IShopRepository shops,
    ITenantWriteScope tenantScope,
    IShopLicenceFence fence,
    ILiveSessionAuthority sessions)
{
    // Entry-point classification is conservative. MembershipLicenceActions classifies the
    // specific change using the live status and whether the requested role differs.
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<MembershipDto>> HandleAsync(
        UpdateShopMembershipCommand command,
        CancellationToken cancellationToken)
    {
        if (!MembershipAuthority.TryParseAssignableRole(command.Role, out var role))
            return Errors.Users.RoleNotSupported;

        if (!MembershipAuthority.TryParseStatus(command.Status, out var status))
            return Errors.Users.MembershipStatusNotSupported;

        if (command.ActorUserId == command.TargetUserId)
            return Errors.Users.CannotModifyOwnMembership;

        await using var transaction = await authority.BeginAsync(cancellationToken);

        // Establish live authority before entering a target that may differ from the active shop.
        var roleAtEntry = await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (roleAtEntry != ShopRole.Owner)
        {
            if (roleAtEntry is not null) return Errors.Shop.UserIsNotOwner;
            return await shops.GetByIdAsync(command.ShopId, cancellationToken) is null
                ? Errors.Shop.ShopNotFound : Errors.Shop.MembershipNotFound;
        }

        await tenantScope.EnterAsync(command.ShopId, cancellationToken);
        var licence = await fence.EnterAsync(command.ShopId, LicenceAction.OperationalCapacity, cancellationToken);
        if (licence.IsError) return licence.Errors;

        // Authority-lock protocol: accounts in ascending id order, then the shop root, then the
        // membership rows. Every decision below is re-read while those locks are held, so a token
        // claim that said "Owner" a minute ago cannot authorize this change.
        await authority.LockUsersAsync([command.ActorUserId, command.TargetUserId], cancellationToken);
        // Revocation takes the same account lock. The session is judged in its own scope, which may
        // be another shop the owner holds or none at all.
        if (!await IsLiveAsync(command, cancellationToken))
            return Errors.Auth.SessionInvalidated;
        if (!await authority.LockShopAsync(command.ShopId, cancellationToken))
            return Errors.Shop.ShopNotFound;

        var actorMembership = await authority.LockMembershipAsync(command.ShopId, command.ActorUserId, cancellationToken);
        if (actorMembership is null || !actorMembership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var actor = await authority.FindUserAsync(command.ActorUserId, cancellationToken);
        if (actor is null || !actor.IsLoginEnabled)
            return Errors.Shop.MembershipNotFound;

        var target = await authority.LockMembershipAsync(command.ShopId, command.TargetUserId, cancellationToken);

        // Every authority lock is now held. The device session is judged again at this instant: one
        // that lapsed while queued behind the shop root neither changes the member nor sees seat usage.
        if (!await IsLiveAsync(command, cancellationToken))
            return Errors.Auth.SessionInvalidated;
        if (target is null)
            return Errors.Users.MembershipNotFound;

        // Ownership is not administered here, in either direction.
        if (target.Role == ShopRole.Owner)
            return Errors.Users.CannotModifyOwner;

        if (target.AuthorityVersion != command.ExpectedVersion)
            return Errors.Users.MembershipVersionConflict;

        var action = MembershipLicenceActions.For(target.Status, status, role != target.Role);
        if (action != LicenceAction.MemberRemoval && licence.Value.SuspensionDenial() is { } denied) return denied;

        // Reinstatement takes a seat; a role change of an Active member or any move out of Active does not.
        if (target.Status != MembershipStatus.Active && status == MembershipStatus.Active
            && await SeatDenialAsync(licence.Value, command.ShopId, cancellationToken) is { } full)
            return full;

        var now = clock.GetUtcNow();
        target.ChangeAuthority(role, status);

        // Any grant still outstanding for this person was issued under the authority that just
        // changed, so it is void in the same transaction as the change that caused it.
        if (target.User?.Email is { Length: > 0 } email)
            await authority.RevokePendingInvitationsAsync(command.ShopId, email, now, cancellationToken);

        if (!await authority.CommitAsync(cancellationToken))
            return Errors.Users.MembershipVersionConflict;

        return new MembershipDto(
            target.UserId,
            target.ShopId,
            DisplayName(target.User?.FirstName, target.User?.LastName),
            MembershipAuthority.ToRoleLabel(target.Role),
            target.Status.ToString(),
            target.AuthorityVersion)
        {
            IsSourceAuthorizedRemoval = action == LicenceAction.MemberRemoval,
        };
    }

    private async Task<bool> IsLiveAsync(UpdateShopMembershipCommand command, CancellationToken ct) =>
        command.Session is { } session && session.UserId == command.ActorUserId
        && await sessions.IsValidAsync(session, ct);

    // Counted under the shop root lock taken above. The owner was re-read under lock, so it may see usage.
    private async Task<Error?> SeatDenialAsync(ShopLicenceLease lease, Guid shopId, CancellationToken ct) =>
        MemberSeatPolicy.Applies(lease)
            ? MemberSeatPolicy.AdmissionDenial(lease, await authority.CountActiveMembersAsync(shopId, ct), discloseUsage: true)
            : null;

    private static string DisplayName(string? firstName, string? lastName) =>
        $"{firstName} {lastName}".Trim();
}
