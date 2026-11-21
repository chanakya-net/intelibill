using ErrorOr;
using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Common.Licensing;

/// <summary>Request-level suspension check for an already authenticated caller.</summary>
public interface ISuspensionAccessReader
{
    /// <summary>Runs one bounded transport handoff while the shop's data access is fenced.</summary>
    Task<Error?> DeliverAsync(Guid shopId, SessionClaims? session, Func<CancellationToken, Task> deliver, CancellationToken ct,
        LicenceAction action = LicenceAction.History, Func<CancellationToken, Task>? prepare = null);

    /// <summary>Reads suspension after the caller has established live authority in the active shop.</summary>
    Task<Error?> ReadActiveShopDenialAsync(Guid shopId, LicenceAction action, CancellationToken ct);

    Task<Error?> ReadDenialAsync(SessionClaims session, Guid shopId, LicenceAction action, CancellationToken ct);
}
