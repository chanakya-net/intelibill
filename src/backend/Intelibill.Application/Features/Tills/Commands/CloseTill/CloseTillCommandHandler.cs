using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Commands.CloseTill;

public sealed class CloseTillCommandHandler(TillLifecycleService service)
{
    [LicenceAction(LicenceAction.TillCompletion)]
    public Task<ErrorOr<TillSessionDto>> HandleAsync(CloseTillCommand command, CancellationToken ct) => service.CloseAsync(command, ct);
}
