namespace Intelibill.Application.Features.Sales.Services.Cancellations;

public interface ISaleCancellationRollout
{
    bool IsPostingEnabled(Guid shopId);
}
