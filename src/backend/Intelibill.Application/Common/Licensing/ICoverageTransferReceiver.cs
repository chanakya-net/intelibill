using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;

namespace Intelibill.Application.Common.Licensing;

public interface ICoverageTransferReceiver
{
    Task<ErrorOr<Success>> ReceiveAsync(TransferCommandV1 command, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct);
}
