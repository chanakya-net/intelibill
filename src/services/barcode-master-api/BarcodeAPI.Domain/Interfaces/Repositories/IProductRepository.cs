using BarcodeAPI.Domain.Entities;

namespace BarcodeAPI.Domain.Interfaces.Repositories;

public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// Look up one or more products by barcode. Uses Dapper for sub-millisecond reads.
    /// </summary>
    Task<List<Product>> GetByBarcodesAsync(List<string> barcodes, CancellationToken ct = default);

    /// <summary>
    /// Inserts a batch of products. Returns a list of failures with reasons.
    /// Successful inserts are (total received - failure count).
    /// </summary>
    Task<List<(string Barcode, string Name, string Reason)>> AddBatchAsync(
        List<Product> products, CancellationToken ct = default);
}
