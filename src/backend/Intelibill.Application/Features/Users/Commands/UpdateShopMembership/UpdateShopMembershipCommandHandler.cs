using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.UpdateShopMembership;

public sealed class UpdateShopMembershipCommandHandler(
    IMembershipAuthorityRepository authority,
    TimeProvider clock)
{
    public async Task<ErrorOr<MembershipDto>> HandleAsync(
        UpdateShopMembershipCommand command,
        CancellationToken cancellationToken)
    {
        if (!MembershipAuthority.TryParseAssignableRole(command.Role, out var role))
            return Errors.Users.RoleNotSupported;

        if (!MembershipAuthority.TryParseStatus(command.Status, out var status))
            return Errors.Users.MembershipStatusNotSupported;

        if (command.ActorUserId == command.TargetUserId)
            return Errors.Users.CannotModifyOwner;

        await using var transaction = await authority.BeginAsync(cancellationToken);

        // Authority-lock protocol: accounts in ascending id order, then the shop root, then the
        // membership rows. Every decision below is re-read while those locks are held, so a token
        // claim that said "Owner" a minute ago cannot authorize this change.
        await authority.LockUsersAsync([command.ActorUserId, command.TargetUserId], cancellationToken);
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
        if (target is null)
            return Errors.Users.MembershipNotFound;

        // Ownership is not administered here, in either direction.
        if (target.Role == ShopRole.Owner)
            return Errors.Users.CannotModifyOwner;

        if (target.AuthorityVersion != command.ExpectedVersion)
            return Errors.Users.MembershipVersionConflict;

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
            target.AuthorityVersion);
    }

    private static string DisplayName(string? firstName, string? lastName) =>
        $"{firstName} {lastName}".Trim();
}
