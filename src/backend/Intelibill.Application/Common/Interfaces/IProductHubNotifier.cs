namespace Intelibill.Application.Common.Interfaces;

public interface IProductHubNotifier
{
    Task NotifyProductCreatedAsync(
        Guid itemId,
        string barcode,
        string name,
        Guid shopId,
        CancellationToken cancellationToken = default);
}
