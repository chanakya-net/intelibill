using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Interfaces;

public interface ITokenService
{
    /// <summary>
    /// Signs an access token for one device session. The shop scope is read from
    /// <paramref name="session"/> rather than passed alongside it, so a token can never name a shop
    /// the session is not scoped to.
    /// </summary>
    /// <remarks>
    /// Asynchronous because signing may be a Key Vault operation rather than a local computation.
    /// </remarks>
    Task<(string AccessToken, DateTimeOffset ExpiresAt)> GenerateAccessTokenAsync(
        User user,
        AuthSession session,
        string? activeShopRole,
        CancellationToken cancellationToken = default);

    RefreshToken CreateRefreshToken(Guid userId);
}
