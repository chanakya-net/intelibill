using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Application.Common.Licensing;

public interface IReductionReservationReceiver
{
    Task<ErrorOr<ReductionObservationResponseV1>> ObserveAsync(ReductionObservationRequestV1 request,
        VerifiedServiceAssertion proof, CancellationToken ct);

    Task<ErrorOr<Success>> ReceiveManifestAsync(ReductionManifestPageV1 page, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct);

    Task<ErrorOr<Success>> ReceiveReservationAsync(ReductionReservationV1 command, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct);
}
