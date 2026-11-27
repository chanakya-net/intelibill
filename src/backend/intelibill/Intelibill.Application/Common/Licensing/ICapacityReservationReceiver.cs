using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Application.Common.Licensing;

public interface ICapacityReservationReceiver
{
    Task<ErrorOr<Success>> ReceiveAsync(CapacityReservationV1 command, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct);
}
