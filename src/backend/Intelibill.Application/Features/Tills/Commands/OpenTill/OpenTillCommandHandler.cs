using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Commands.OpenTill;

public sealed class OpenTillCommandHandler(TillLifecycleService service)
{
    public Task<ErrorOr<TillSessionDto>> HandleAsync(OpenTillCommand command, CancellationToken ct) => service.OpenAsync(command, ct);
}
