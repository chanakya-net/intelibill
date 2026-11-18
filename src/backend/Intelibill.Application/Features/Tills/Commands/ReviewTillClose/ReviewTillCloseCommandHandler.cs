using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Tills.Services;

namespace Intelibill.Application.Features.Tills.Commands.ReviewTillClose;

public sealed class ReviewTillCloseCommandHandler(TillLifecycleService service)
{
    [LicenceAction(LicenceAction.TillCompletion)]
    public Task<ErrorOr<TillSessionDto>> HandleAsync(ReviewTillCloseCommand command, CancellationToken ct) => service.ReviewAsync(command, ct);
}
