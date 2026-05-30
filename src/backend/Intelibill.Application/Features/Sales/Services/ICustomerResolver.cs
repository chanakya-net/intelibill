using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services;

public interface ICustomerResolver
{
    Task<ErrorOr<Customer?>> ResolveAsync(
        Guid shopId,
        Guid? customerId,
        string? customerPhone,
        bool hasDueAmount,
        PaymentMethod paymentMethod,
        CancellationToken cancellationToken);
}
