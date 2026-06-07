namespace Intelibill.Application.Features.PurchaseOrders.Services;

public interface IPurchaseOrderReceiptNumberGenerator
{
    Task<string> GenerateAsync(Guid shopId, int year, CancellationToken cancellationToken = default);
}
