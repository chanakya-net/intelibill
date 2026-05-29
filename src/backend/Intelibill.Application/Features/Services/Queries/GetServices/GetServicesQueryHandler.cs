using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Services.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Services.Queries.GetServices;

public sealed class GetServicesQueryHandler(
    IUserRepository userRepository,
    IServiceRepository serviceRepository)
{
    public async Task<ErrorOr<IReadOnlyList<ServiceDto>>> HandleAsync(GetServicesQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (callerMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Service.UserIsNotOwnerOrManager;

        var services = await serviceRepository.GetByShopIdAsync(
            query.ActiveShopId,
            query.IncludeInactive,
            query.Search,
            cancellationToken);

        return services.Select(s => new ServiceDto(
            s.Id,
            s.Code,
            s.Name,
            s.Description,
            s.Price,
            s.HsnCode,
            s.TaxRatePercent,
            s.TaxIncluded,
            s.IsActive)).ToList();
    }
}
