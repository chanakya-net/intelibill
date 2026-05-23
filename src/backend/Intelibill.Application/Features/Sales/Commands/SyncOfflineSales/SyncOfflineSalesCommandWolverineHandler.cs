using ErrorOr;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed class SyncOfflineSalesCommandWolverineHandler(IServiceProvider serviceProvider)
{
    public async Task<ErrorOr<OfflineSalesSyncResponseDto>> HandleAsync(
        SyncOfflineSalesCommand command,
        CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetRequiredService<SyncOfflineSalesCommandHandler>();
        return await handler.HandleAsync(command, cancellationToken);
    }
}
