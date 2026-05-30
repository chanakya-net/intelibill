namespace Intelibill.Infrastructure.Options;

public sealed class ProductLookupOptions
{
    public const string SectionName = "ProductLookup";

    public string BaseUrl { get; init; } = "http://localhost:5209";

    public string LookupPath { get; init; } = "/api/products/lookup";

    public string ApiKey { get; init; } = "dev-api-key-change-in-production";
}
