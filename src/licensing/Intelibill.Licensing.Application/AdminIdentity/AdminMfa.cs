using ErrorOr;

namespace Intelibill.Licensing.Application.AdminIdentities;

public interface IAdminMfaEnrollment
{
    Task<ErrorOr<AdminMfaSetup>> StartAsync(Guid adminId, string credentialDigest, CancellationToken cancellationToken);
    Task<ErrorOr<AdminMfaCompletion>> CompleteAsync(
        Guid adminId, string credentialDigest, string? otp, CancellationToken cancellationToken);
}

/// <summary>Consumes MFA proof for an administrator already proved by the caller.
/// The caller owns password/session authorization and any recovery workflow.</summary>
public interface IAdminMfaVerifier
{
    Task<ErrorOr<AdminMfaProof>> ConsumeTotpAsync(Guid provedAdminId, string? otp, CancellationToken cancellationToken);
    Task<ErrorOr<AdminMfaProof>> ConsumeRecoveryCodeAsync(
        Guid provedAdminId, string? recoveryCode, CancellationToken cancellationToken);
}

public sealed class AdminMfaSetup(string setupSecret)
{
    public string SetupSecret { get; } = setupSecret;
}

public sealed class AdminMfaCompletion(IReadOnlyList<string> recoveryCodes)
{
    public IReadOnlyList<string> RecoveryCodes { get; } = recoveryCodes;
}

public sealed record AdminMfaProof(DateTimeOffset VerifiedAt);
