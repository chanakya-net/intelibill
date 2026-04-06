using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Queries.GetSuppliers;

public sealed class GetSuppliersQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository)
{
    public async Task<ErrorOr<IReadOnlyList<SupplierDto>>> HandleAsync(GetSuppliersQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        var shop = await shopRepository.GetByIdWithMembersAsync(query.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var ownerMembership = shop.Memberships.FirstOrDefault(sm => sm.Role == ShopRole.Owner);
        if (ownerMembership is null)
            return Errors.Supplier.ShopOwnerNotFound;

        var suppliers = await supplierRepository.GetByOwnerUserIdAsync(ownerMembership.UserId, cancellationToken);

        var result = new List<SupplierDto>();
        foreach (var s in suppliers)
        {
            var balance = await supplierLedgerEntryRepository.GetSupplierBalanceAsync(query.ActiveShopId, s.Id, cancellationToken);
            result.Add(new SupplierDto(
                s.Id,
                s.Name,
                s.ContactPersonName,
                s.ContactPersonPhone,
                s.Address,
                s.City,
                s.State,
                s.Pin,
                s.Amount,
                s.Status,
                s.IsActive,
                s.IsPreferred,
                balance));
        }

        return result;
    }
}