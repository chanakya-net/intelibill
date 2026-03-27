using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Interfaces;

public interface ITokenService
{
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user, Guid? activeShopId = null);
    RefreshToken CreateRefreshToken(Guid userId);
}
