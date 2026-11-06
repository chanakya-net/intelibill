using Intelibill.Application.Features.Sales.Services;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SaleReturnNumberGeneratorTests
{
    [Fact]
    public void Generate_UsesReturnPrefixBusinessDateAndEightCharacterSuffix()
    {
        var returnNumber = new SaleReturnNumberGenerator().Generate(new DateOnly(2026, 10, 21));

        Assert.Matches("^RET-20261021-[0-9A-F]{8}$", returnNumber);
    }
}
