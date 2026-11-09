using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Queries.GetSuppliers;

public sealed class GetSuppliersQueryHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository)
{
    public async Task<ErrorOr<IReadOnlyList<SupplierDto>>> HandleAsync(GetSuppliersQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId && sm.IsActive);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        var suppliers = await supplierRepository.GetByShopIdAsync(query.ActiveShopId, query.IncludeSystem, cancellationToken);

        return (await SupplierReadMapping.EnrichAsync(query.ActiveShopId, suppliers, supplierLedgerEntryRepository, cancellationToken)).ToErrorOr();
    }
}
