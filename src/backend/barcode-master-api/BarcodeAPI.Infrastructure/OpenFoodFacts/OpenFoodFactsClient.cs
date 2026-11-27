using System.Text.Json;
using BarcodeAPI.Domain.Entities;
using BarcodeAPI.Domain.Interfaces.Clients;

namespace BarcodeAPI.Infrastructure.OpenFoodFacts;

public sealed class OpenFoodFactsClient(HttpClient httpClient) : IOpenFoodFactsClient
{
    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"api/v2/product/{Uri.EscapeDataString(barcode)}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        if (!root.TryGetProperty("status", out var statusElement) || statusElement.GetInt32() != 1)
        {
            return null;
        }

        var productName = GetProductName(root);
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }

        return new Product(barcode.Trim(), productName.Trim());
    }

    private static string? GetProductName(JsonElement root)
    {
        if (root.TryGetProperty("product_name", out var productNameElement) &&
            productNameElement.ValueKind == JsonValueKind.String)
        {
            return productNameElement.GetString();
        }

        if (root.TryGetProperty("product", out var productElement) &&
            productElement.ValueKind == JsonValueKind.Object &&
            productElement.TryGetProperty("product_name", out var nestedProductNameElement) &&
            nestedProductNameElement.ValueKind == JsonValueKind.String)
        {
            return nestedProductNameElement.GetString();
        }

        return null;
    }
}
