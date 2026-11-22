using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Services.Commands.UpdateService;

public sealed class UpdateServiceCommandHandler(
    IUserRepository userRepository,
    IServiceRepository serviceRepository,
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<Success>> HandleAsync(UpdateServiceCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Service.UserIsNotOwnerOrManager;

        return await PostingTransaction.RunAsync<Success>(unitOfWork,
            ct => EditAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<Success>> EditAsync(UpdateServiceCommand command, CancellationToken cancellationToken)
    {
        // The definition already exists. Preserve the existing edit contract and serialize suspension.
        var licence = await licenceFence.EnterForSessionAsync(authority, command.Session, command.ActorUserId,
            command.ActiveShopId, LicenceAction.OperationalCapacity, cancellationToken);
        if (licence.IsError) return licence.Errors;
        if (licence.Value.SuspensionDenial() is { } denial) return denial;

        var service = await serviceRepository.GetByIdAsync(command.ServiceId, cancellationToken);
        if (service is null || service.ShopId != command.ActiveShopId)
            return Errors.Service.NotFound;

        var normalizedName = command.Name.Trim();
        if (!string.Equals(normalizedName, service.Name, StringComparison.Ordinal))
        {
            var existingByName = await serviceRepository.GetByNameAsync(command.ActiveShopId, normalizedName, cancellationToken);
            if (existingByName is not null)
                return Errors.Service.NameAlreadyExists;
        }

        service.Update(
            normalizedName,
            command.Description,
            command.Price,
            command.HsnCode,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.ActorUserId);

        serviceRepository.Update(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
