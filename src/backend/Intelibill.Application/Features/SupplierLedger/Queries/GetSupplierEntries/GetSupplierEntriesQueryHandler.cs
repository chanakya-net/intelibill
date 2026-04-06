using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierLedger.Queries.GetSupplierEntries;

public sealed class GetSupplierEntriesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository ledgerRepository)
{
    public async Task<ErrorOr<IReadOnlyList<SupplierLedgerEntryDto>>> HandleAsync(GetSupplierEntriesQuery query, CancellationToken cancellationToken)
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

        var supplier = await supplierRepository.GetByIdAsync(query.SupplierId, cancellationToken);
        if (supplier is null)
            return Errors.Supplier.SupplierNotFound;

        if (supplier.OwnerUserId != ownerMembership.UserId)
            return Errors.Supplier.SupplierNotFound;

        var entries = await ledgerRepository.GetBySupplierAsync(query.ActiveShopId, query.SupplierId, cancellationToken);

        return entries
            .Select(e => new SupplierLedgerEntryDto(
                e.Id,
                e.SupplierId,
                e.EntryType,
                e.Amount,
                e.EntryDate,
                e.Notes))
            .ToList();
    }
}
