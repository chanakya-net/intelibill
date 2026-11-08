using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;

public sealed class RecordTillCashMovementCommandHandler(TillLifecycleService service)
{
    public Task<ErrorOr<TillSessionDto>> HandleAsync(RecordTillCashMovementCommand command, CancellationToken ct) => service.MoveAsync(command, ct);
}
