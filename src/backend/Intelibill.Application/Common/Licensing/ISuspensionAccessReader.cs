using ErrorOr;
using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Common.Licensing;

/// <summary>Request-level suspension check for an already authenticated caller.</summary>
public interface ISuspensionAccessReader
{
    Task<Error?> ReadDenialAsync(SessionClaims session, Guid shopId, LicenceAction action, CancellationToken ct);
}
