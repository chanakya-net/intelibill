using System.Globalization;

namespace Intelibill.Application.Features.Sales.Services;

public sealed class SaleReturnNumberGenerator : ISaleReturnNumberGenerator
{
    public string Generate(DateOnly businessDate) =>
        string.Create(CultureInfo.InvariantCulture, $"RET-{businessDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");
}
