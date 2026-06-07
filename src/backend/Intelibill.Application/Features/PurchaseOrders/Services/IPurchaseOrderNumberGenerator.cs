namespace Intelibill.Application.Features.PurchaseOrders.Services;

public interface IPurchaseOrderNumberGenerator
{
    Task<string> GenerateAsync(Guid shopId, int year, CancellationToken cancellationToken = default);
}
