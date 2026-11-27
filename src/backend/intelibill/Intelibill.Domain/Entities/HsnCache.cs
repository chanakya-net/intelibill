using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed record HsnTaxScenario(string Condition, string TaxPercentage);

public sealed class HsnCache : BaseEntity
{
    public string ProductName { get; private set; } = string.Empty;
    public List<string> HsnCodes { get; private set; } = [];
    public List<HsnTaxScenario> TaxScenarios { get; private set; } = [];
    public DateTime CachedAt { get; private set; }

    private HsnCache() { }

    public static HsnCache Create(
        string productName,
        IReadOnlyList<string> hsnCodes,
        IReadOnlyList<HsnTaxScenario> taxScenarios)
    {
        return new HsnCache
        {
            ProductName = productName,
            HsnCodes = [.. hsnCodes],
            TaxScenarios = [.. taxScenarios],
            CachedAt = DateTime.UtcNow,
        };
    }
}
