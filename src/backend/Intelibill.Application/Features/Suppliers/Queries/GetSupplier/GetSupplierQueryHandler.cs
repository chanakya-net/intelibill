using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Queries.GetSupplier;

public sealed class GetSupplierQueryHandler(IUserRepository users, ISupplierRepository suppliers, ISupplierLedgerEntryRepository ledger)
{
    public async Task<ErrorOr<SupplierDto>> HandleAsync(GetSupplierQuery query, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (user is null) return Errors.Auth.UserNotFound;
        if (!user.ShopMemberships.Any(m => m.ShopId == query.ShopId && m.IsActive)) return Errors.Shop.MembershipNotFound;
        var supplier = (await suppliers.FindAsync(s => s.ShopId == query.ShopId && s.Id == query.SupplierId, cancellationToken)).SingleOrDefault();
        if (supplier is null || supplier.ShopId != query.ShopId) return Errors.Supplier.SupplierNotFound;
        return (await SupplierReadMapping.EnrichAsync(query.ShopId, [supplier], ledger, cancellationToken))[0];
    }
}
