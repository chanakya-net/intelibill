using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Services.Commands.DeactivateService;

public sealed class DeactivateServiceCommandHandler(
    IUserRepository userRepository,
    IServiceRepository serviceRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Success>> HandleAsync(DeactivateServiceCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Service.UserIsNotOwnerOrManager;

        var service = await serviceRepository.GetByIdAsync(command.ServiceId, cancellationToken);
        if (service is null || service.ShopId != command.ActiveShopId)
            return Errors.Service.NotFound;

        service.Deactivate(command.ActorUserId);

        serviceRepository.Update(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
