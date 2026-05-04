namespace Intelibill.Application.Features.Sales.Services;

public sealed class SaleReturnNumberGenerator : ISaleReturnNumberGenerator
{
    public string Generate(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return $"RET-{timestamp.UtcDateTime:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }
}
