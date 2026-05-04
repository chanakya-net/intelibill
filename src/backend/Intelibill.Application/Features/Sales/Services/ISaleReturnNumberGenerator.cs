namespace Intelibill.Application.Features.Sales.Services;

public interface ISaleReturnNumberGenerator
{
    string Generate(DateTimeOffset? now = null);
}
