using Intelibill.Application.Features.Sales.Services;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SaleReturnNumberGeneratorTests
{
    [Fact]
    public void Generate_UsesReturnPrefixUtcDateAndEightCharacterSuffix()
    {
        var generator = new SaleReturnNumberGenerator();
        var now = new DateTimeOffset(2026, 5, 4, 10, 15, 0, TimeSpan.Zero);

        var returnNumber = generator.Generate(now);

        Assert.Matches("^RET-20260504-[0-9A-F]{8}$", returnNumber);
    }
}
