using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed record ExternalOAuthState(ExternalAuthProvider Provider, string? CodeVerifier);

internal interface IExternalOAuthStateStore
{
    Task StoreAsync(string state, ExternalOAuthState value, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<ExternalOAuthState?> ConsumeAsync(string state, CancellationToken cancellationToken = default);
}