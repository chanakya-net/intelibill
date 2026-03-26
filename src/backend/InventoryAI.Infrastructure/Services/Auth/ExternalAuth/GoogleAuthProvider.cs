using ErrorOr;
using Google.Apis.Auth;
using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Application.Common.Models;
using InventoryAI.Domain.Enums;
using InventoryAI.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace InventoryAI.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class GoogleAuthProvider(IOptions<ExternalAuthOptions> options) : IExternalAuthProvider
{
    private readonly ExternalAuthOptions.GoogleOptions _google = options.Value.Google;

    public ExternalAuthProvider Provider => ExternalAuthProvider.Google;

    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_google.ClientId],
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);

            var parts = (payload.Name ?? string.Empty).Split(' ', 2);
            var firstName = parts.Length > 0 ? parts[0] : string.Empty;
            var lastName = parts.Length > 1 ? parts[1] : string.Empty;

            return new ExternalUserInfo(
                ProviderKey: payload.Subject,
                Email: payload.Email,
                FirstName: firstName,
                LastName: lastName);
        }
        catch (InvalidJwtException ex)
        {
            return Error.Unauthorized("Auth.Google.InvalidToken", ex.Message);
        }
    }
}
