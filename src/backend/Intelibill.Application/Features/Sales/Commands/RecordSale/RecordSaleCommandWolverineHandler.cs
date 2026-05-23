using ErrorOr;
using Intelibill.Application.Features.Sales.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandWolverineHandler(IServiceProvider serviceProvider)
{
    public async Task<ErrorOr<SaleDto>> HandleAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetRequiredService<RecordSaleCommandHandler>();
        return await handler.HandleAsync(command, cancellationToken);
    }
}
