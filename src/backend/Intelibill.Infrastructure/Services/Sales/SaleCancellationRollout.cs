using Intelibill.Application.Features.Sales.Services.Cancellations;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
namespace Intelibill.Infrastructure.Services.Sales;

internal sealed class SaleCancellationRollout(IOptionsMonitor<SaleCancellationOptions> options) : ISaleCancellationRollout
{
    public bool IsPostingEnabled(Guid shopId) => options.CurrentValue.Enabled && options.CurrentValue.AllowedShopIds.Contains(shopId);
}
