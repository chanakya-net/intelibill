using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class HsnCacheTests
{
    [Fact]
    public void Create_WithValidData_SetsAllProperties()
    {
        var hsnCodes = new List<string> { "30049069" };
        var taxScenarios = new List<HsnTaxScenario>
        {
            new("General", "12%")
        };

        var cache = HsnCache.Create("Paracetamol", hsnCodes, taxScenarios);

        Assert.Equal("Paracetamol", cache.ProductName);
        Assert.Single(cache.HsnCodes);
        Assert.Equal("30049069", cache.HsnCodes[0]);
        Assert.Single(cache.TaxScenarios);
        Assert.Equal("General", cache.TaxScenarios[0].Condition);
        Assert.Equal("12%", cache.TaxScenarios[0].TaxPercentage);
    }

    [Fact]
    public void Create_WithMultipleHsnCodes_StoresAll()
    {
        var hsnCodes = new List<string> { "30049069", "30049070", "30049071" };

        var cache = HsnCache.Create("Paracetamol", hsnCodes, []);

        Assert.Equal(3, cache.HsnCodes.Count);
        Assert.Contains("30049069", cache.HsnCodes);
        Assert.Contains("30049070", cache.HsnCodes);
        Assert.Contains("30049071", cache.HsnCodes);
    }

    [Fact]
    public void Create_WithMultipleTaxScenarios_StoresAll()
    {
        var taxScenarios = new List<HsnTaxScenario>
        {
            new("General", "12%"),
            new("Essential medicines", "0%"),
            new("Luxury", "18%")
        };

        var cache = HsnCache.Create("Paracetamol", [], taxScenarios);

        Assert.Equal(3, cache.TaxScenarios.Count);
    }

    [Fact]
    public void Create_SetsCachedAtToNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var cache = HsnCache.Create("Paracetamol", [], []);

        var after = DateTime.UtcNow.AddSeconds(1);
        Assert.True(cache.CachedAt >= before && cache.CachedAt <= after);
    }
}
