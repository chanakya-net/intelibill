using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Answers whether a signed token still means anything, against the live session, account and
/// membership behind it.
/// </summary>
/// <remarks>
/// Request authentication reaches the implementation directly. This port exists so the parts that
/// are not a request — a hub admitting a transport, a dispatcher deciding a recipient, a sweep
/// deciding whether to close a socket — can ask the same question without taking a dependency on
/// Infrastructure.
/// </remarks>
public interface ILiveSessionAuthority
{
    Task<bool> IsValidAsync(SessionClaims claims, CancellationToken cancellationToken = default);
}
