using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;

public sealed class RecordTillCashMovementCommandHandler(TillLifecycleService service)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public Task<ErrorOr<TillSessionDto>> HandleAsync(RecordTillCashMovementCommand command, CancellationToken ct) => service.MoveAsync(command, ct);
}
