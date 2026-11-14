using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal sealed class AdminMfaVerifier(AdminMfaStore store, AdminMfaKeys keys, TimeProvider clock) : IAdminMfaVerifier
{
    public Task<ErrorOr<AdminMfaProof>> ConsumeTotpAsync(
        Guid provedAdminId, string? otp, CancellationToken cancellationToken) =>
        WithEnrolledAsync(provedAdminId, (totp, now) =>
        {
            var match = AdminMfaEnrollmentService.VerifyStep(keys, totp, otp, now);
            if (match.IsError) return Task.FromResult<ErrorOr<AdminMfaProof>>(match.Errors);
            totp.AcceptStep(match.Value);
            return Task.FromResult<ErrorOr<AdminMfaProof>>(new AdminMfaProof(now));
        }, cancellationToken);

    public Task<ErrorOr<AdminMfaProof>> ConsumeRecoveryCodeAsync(
        Guid provedAdminId, string? recoveryCode, CancellationToken cancellationToken) =>
        WithEnrolledAsync(provedAdminId, async (totp, now) =>
        {
            var normalized = NormalizeRecoveryCode(recoveryCode);
            if (normalized is null) return InvalidRecovery;
            var digest = keys.HashRecovery(totp, normalized);
            if (digest is null) return AdminMfaEnrollmentService.KeyUnavailable;
            var code = await store.ReadRecoveryAsync(provedAdminId, digest, cancellationToken);
            if (code is null) return InvalidRecovery;
            code.Consume(now);
            totp.ResetAttempts();
            return new AdminMfaProof(now);
        }, cancellationToken);

    private Task<ErrorOr<AdminMfaProof>> WithEnrolledAsync(Guid adminId,
        Func<AdminTotp, DateTimeOffset, Task<ErrorOr<AdminMfaProof>>> operation,
        CancellationToken cancellationToken) =>
        store.WithAdminAsync<AdminMfaProof>(adminId, async admin =>
        {
            if (admin.MfaEnrolledAt is null) return AdminMfaEnrollmentService.Refused;
            var totp = await store.ReadTotpAsync(adminId, cancellationToken);
            if (totp?.LastAcceptedStep is null) return AdminMfaEnrollmentService.Refused;
            var now = clock.GetUtcNow();
            if (totp.TakeAttempt(now) is { } retryAt) return AdminMfaEnrollmentService.Throttled(retryAt);
            return await operation(totp, now);
        }, cancellationToken);

    private static Error InvalidRecovery => Error.Validation("Mfa.InvalidRecoveryCode", "The recovery code could not be verified.");

    private static string? NormalizeRecoveryCode(string? input)
    {
        if (input is null || input.Length is not (32 or 35)) return null;
        var normalized = input.Replace("-", "", StringComparison.Ordinal);
        return normalized.Length == 32 && normalized.All(char.IsAsciiHexDigit) ? normalized.ToUpperInvariant() : null;
    }
}
