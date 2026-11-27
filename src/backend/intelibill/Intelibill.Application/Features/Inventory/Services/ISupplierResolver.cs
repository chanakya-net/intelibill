using ErrorOr;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Inventory.Services;

public interface ISupplierResolver
{
    Task<ErrorOr<Supplier>> ResolveAsync(Guid shopId, Guid? requestedSupplierId, CancellationToken cancellationToken);
}
