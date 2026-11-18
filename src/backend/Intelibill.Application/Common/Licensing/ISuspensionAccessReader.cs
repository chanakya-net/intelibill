using ErrorOr;
using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Common.Licensing;

/// <summary>Request-level suspension check for an already authenticated caller.</summary>
public interface ISuspensionAccessReader
{
    Task<bool> CanReceiveRealtimeAsync(Guid shopId, CancellationToken ct);

    /// <summary>Reads suspension after the caller has established live authority in the active shop.</summary>
    Task<Error?> ReadActiveShopDenialAsync(Guid shopId, LicenceAction action, CancellationToken ct);

    Task<Error?> ReadDenialAsync(SessionClaims session, Guid shopId, LicenceAction action, CancellationToken ct);
}
