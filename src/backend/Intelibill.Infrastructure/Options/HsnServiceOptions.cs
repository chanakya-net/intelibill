namespace Intelibill.Infrastructure.Options;

public sealed class HsnServiceOptions
{
    public const string SectionName = "HsnService";

    public string BaseUrl { get; init; } = "http://localhost:5001";

    public string LookupPath { get; init; } = "/api/Hsn";

    public string ApiKey { get; init; } = "dev-hsn-key-change-in-production";
}
