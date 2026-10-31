using ErrorOr;
using Intelibill.Application.Features.Auth.DTOs;
namespace Intelibill.Application.Common.Interfaces;

public interface IEmailOwnershipService
{
    Task<ErrorOr<Guid>> RequestAsync(string email, CancellationToken ct);
    Task<ErrorOr<Guid>> RequestClaimAsync(string email, CancellationToken ct);
    Task<ErrorOr<AuthResult>> ConfirmEnrollmentAsync(Guid challengeId, string token, string password, string firstName, string lastName, CancellationToken ct);
    Task<ErrorOr<AuthResult>> ConfirmClaimAsync(Guid challengeId, string token, string password, string firstName, string lastName, CancellationToken ct);
    Task<ErrorOr<ReauthenticationResult>> ReauthenticateAsync(Guid actor, long version, string? password, CancellationToken ct);
    Task<ErrorOr<Guid>> RequestEmailChangeAsync(Guid actor, long version, string email, string proof, CancellationToken ct);
    Task<ErrorOr<bool>> ConfirmEmailChangeAsync(Guid actor, long version, Guid challengeId, string token, CancellationToken ct);
}
public sealed record ReauthenticationResult(string Proof, DateTimeOffset ExpiresAt);
