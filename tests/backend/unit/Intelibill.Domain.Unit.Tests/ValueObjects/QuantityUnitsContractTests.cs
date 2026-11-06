using System.Text.Json;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public class QuantityUnitsContractTests
{
    [Fact]
    public void catalog_matches_shared_contract()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ContractPath()));
        var root = doc.RootElement;
        Assert.Equal("quantity-units/1", root.GetProperty("contract").GetString());
        Assert.Equal(QuantityUnits.MaxScale, root.GetProperty("maxScale").GetInt32());
        Assert.Equal("NOS", root.GetProperty("customDocumentUnit").GetProperty("whole").GetString());
        Assert.Equal("OTH", root.GetProperty("customDocumentUnit").GetProperty("fractional").GetString());

        var units = root.GetProperty("units").EnumerateArray().ToList();
        Assert.Equal(units.Count, QuantityUnits.All.Count);
        for (var i = 0; i < units.Count; i++)
        {
            var expected = units[i];
            var actual = QuantityUnits.All[i];
            Assert.Equal(expected.GetProperty("code").GetString(), actual.Code);
            Assert.Equal(expected.GetProperty("scale").GetInt32(), actual.Scale);
            Assert.Equal(expected.GetProperty("documentUnit").GetString(), actual.DocumentUnit);
            Assert.Equal(
                expected.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!).ToList(),
                actual.Aliases.ToList());
        }
    }

    [Fact]
    public void resolve_matches_trimmed_case_insensitive_aliases()
    {
        Assert.Equal("KG", QuantityUnits.Resolve(" kg ")?.Code);
        Assert.Equal("KG", QuantityUnits.Resolve("Kgs")?.Code);
        Assert.Null(QuantityUnits.Resolve("BAGGIE"));
        Assert.Null(QuantityUnits.Resolve(null));
    }

    [Fact]
    public void document_unit_falls_back_for_custom_labels()
    {
        Assert.Equal("NOS", QuantityUnits.DocumentUnitFor(null, 0));
        Assert.Equal("OTH", QuantityUnits.DocumentUnitFor(null, 2));
        Assert.Equal("KGS", QuantityUnits.DocumentUnitFor("KG", 3));
    }

    private static string ContractPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "contracts")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "contracts", "quantity-units-v1.json");
    }
}
