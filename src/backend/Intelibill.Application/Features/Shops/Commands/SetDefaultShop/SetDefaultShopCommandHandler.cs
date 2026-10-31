using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.SetDefaultShop;

public sealed class SetDefaultShopCommandHandler(
    IUserRepository userRepository,
    IAuthSessionRepository sessions,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopPreferenceResult>> HandleAsync(SetDefaultShopCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var targetMembership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (targetMembership is null)
            return Errors.Shop.MembershipNotFound;

        // One transaction, because clearing the old default and setting the new one are two writes
        // the partial unique index cannot see at once — and the account row is locked inside it, so
        // a preference cannot be written by a session a concurrent password change or disablement
        // has already invalidated.
        await using var transaction = await sessions.BeginAsync(cancellationToken);

        var authority = await sessions.LockUserAuthorityAsync(command.UserId, cancellationToken);
        if (authority is null || !authority.CanHoldSession || authority.SecurityVersion != command.SecurityVersion)
            return Errors.Auth.SessionInvalidated;

        if (!targetMembership.IsDefault)
        {
            foreach (var membership in user.ShopMemberships.Where(sm => sm.IsDefault))
                membership.SetDefault(false);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            targetMembership.SetDefault(true);
        }

        return await sessions.CommitAsync(cancellationToken)
            ? new ShopPreferenceResult(command.ActiveShopId, AuthShopSelection.Describe(user))
            : Errors.Auth.SessionInvalidated;
    }
}
