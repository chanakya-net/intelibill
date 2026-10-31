using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Unit.Tests;

/// <summary>
/// A stand-in for what the session service returns, so handler tests can assert what they hand it
/// rather than how it mints tokens — which is tested where it is implemented.
/// </summary>
internal static class AuthResults
{
    internal static AuthResult For(User user) =>
        new(
            "accessToken",
            "refreshToken",
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7),
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language),
            null,
            [],
            Guid.NewGuid());

    internal static AccessScopeResult Scope(Guid? activeShopId, long scopeVersion = 1) =>
        new("accessToken", DateTimeOffset.UtcNow.AddMinutes(15), activeShopId, scopeVersion, []);
}
