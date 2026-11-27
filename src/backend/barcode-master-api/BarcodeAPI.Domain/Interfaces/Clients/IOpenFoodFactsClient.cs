using BarcodeAPI.Domain.Entities;

namespace BarcodeAPI.Domain.Interfaces.Clients;

public interface IOpenFoodFactsClient
{
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
}
