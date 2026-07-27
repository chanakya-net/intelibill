using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Interfaces;

public interface ITokenService
{
    /// <summary>
    /// Asynchronous because signing may be a Key Vault operation rather than a
    /// local computation.
    /// </summary>
    Task<(string AccessToken, DateTimeOffset ExpiresAt)> GenerateAccessTokenAsync(
        User user,
        Guid? activeShopId = null,
        string? activeShopRole = null,
        CancellationToken cancellationToken = default);

    RefreshToken CreateRefreshToken(Guid userId);
}
