using ErrorOr;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Enums;
namespace Intelibill.Application.Common.Interfaces;

public interface IEmailOwnershipService
{
    Task<ErrorOr<Guid>> RequestAsync(string email, CancellationToken ct);
    Task<ErrorOr<Guid>> RequestClaimAsync(string email, CancellationToken ct);
    Task<ErrorOr<AuthResult>> ConfirmEnrollmentAsync(Guid challengeId, string token, string password, string firstName, string lastName, CancellationToken ct);
    Task<ErrorOr<AuthResult>> ConfirmClaimAsync(Guid challengeId, string token, string password, string firstName, string lastName, CancellationToken ct);
    Task<ErrorOr<ReauthenticationResult>> ReauthenticateAsync(Guid actor, long version, string? password, ExternalAuthProvider? provider, string? token, CancellationToken ct);
    Task<ErrorOr<Guid>> RequestEmailChangeAsync(Guid actor, long version, string email, string proof, CancellationToken ct);
    Task<ErrorOr<bool>> ConfirmEmailChangeAsync(Guid actor, long version, Guid challengeId, string token, CancellationToken ct);
    Task<ErrorOr<bool>> LinkAsync(Guid actor, long version, ExternalAuthProvider provider, string token, string proof, CancellationToken ct);
    Task<ErrorOr<bool>> UnlinkAsync(Guid actor, long version, ExternalAuthProvider provider, string proof, CancellationToken ct);
}
public sealed record ReauthenticationResult(string Proof, DateTimeOffset ExpiresAt);
