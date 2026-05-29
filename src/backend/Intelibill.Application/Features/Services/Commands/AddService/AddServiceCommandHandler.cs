using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Services.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Services.Commands.AddService;

public sealed class AddServiceCommandHandler(
    IUserRepository userRepository,
    IServiceRepository serviceRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ServiceDto>> HandleAsync(AddServiceCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Service.UserIsNotOwnerOrManager;

        var normalizedName = command.Name.Trim();
        if (await serviceRepository.GetByNameAsync(command.ActiveShopId, normalizedName, cancellationToken) is not null)
            return Errors.Service.NameAlreadyExists;

        var code = await serviceRepository.GetNextCodeAsync(command.ActiveShopId, cancellationToken);

        if (await serviceRepository.GetByCodeAsync(command.ActiveShopId, code, cancellationToken) is not null)
            return Errors.Service.CodeAlreadyExists;

        var service = Service.Create(
            command.ActiveShopId,
            code,
            normalizedName,
            command.Description,
            command.Price,
            command.HsnCode,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.IsActive,
            command.ActorUserId);

        await serviceRepository.AddAsync(service, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ServiceDto(
            service.Id,
            service.Code,
            service.Name,
            service.Description,
            service.Price,
            service.HsnCode,
            service.TaxRatePercent,
            service.TaxIncluded,
            service.IsActive);
    }
}
