using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

internal sealed class SupplierResolver(ISupplierRepository supplierRepository) : ISupplierResolver
{
    public async Task<ErrorOr<Supplier>> ResolveAsync(Guid shopId, Guid? requestedSupplierId, CancellationToken cancellationToken)
    {
        if (requestedSupplierId is Guid supplierId)
        {
            var supplier = await supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (supplier is null || supplier.ShopId != shopId)
                return Errors.Supplier.SupplierNotFound;

            return supplier;
        }

        var systemSupplier = await supplierRepository.GetSystemByShopIdAsync(shopId, cancellationToken);
        if (systemSupplier is null)
            return Errors.Supplier.SystemSupplierNotFound;

        return systemSupplier;
    }
}
