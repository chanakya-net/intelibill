using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.SetDefaultShop;

public sealed class SetDefaultShopCommandHandler(
    IUserRepository userRepository,
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

        if (!targetMembership.IsDefault)
        {
            // Two writes, because the partial unique index must never see two defaults — inside one
            // transaction, so a failure between them cannot leave the account with no default at
            // all.
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var membership in user.ShopMemberships.Where(sm => sm.IsDefault))
                    membership.SetDefault(false);

                await unitOfWork.SaveChangesAsync(cancellationToken);

                targetMembership.SetDefault(true);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }

        return new ShopPreferenceResult(command.ActiveShopId, AuthShopSelection.Describe(user));
    }
}
