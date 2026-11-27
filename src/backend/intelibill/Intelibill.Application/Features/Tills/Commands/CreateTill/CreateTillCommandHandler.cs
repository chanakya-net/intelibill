using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Commands.CreateTill;

public sealed class CreateTillCommandHandler(TillLifecycleService service)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public Task<ErrorOr<TillDto>> HandleAsync(CreateTillCommand command, CancellationToken ct) => service.CreateAsync(command, ct);
}
