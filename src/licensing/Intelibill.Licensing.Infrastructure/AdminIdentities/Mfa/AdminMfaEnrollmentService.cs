using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal sealed class AdminMfaEnrollmentService(AdminMfaStore store, IBootstrapStore bootstrap, AdminMfaKeys keys, TimeProvider clock)
    : IAdminMfaEnrollment
{
    internal static readonly Error Refused = Error.Unauthorized("Mfa.Unavailable", "Authenticator enrollment or verification is unavailable.");
    internal static readonly Error KeyUnavailable = Error.Failure("Mfa.KeyUnavailable", "Authenticator verification is temporarily unavailable.");

    public Task<ErrorOr<AdminMfaSetup>> StartAsync(Guid adminId, string credentialDigest, CancellationToken cancellationToken) =>
        WithPendingAsync<AdminMfaSetup>(adminId, credentialDigest, async (_, _) =>
        {
            var totp = await store.ReadTotpAsync(adminId, cancellationToken);
            return totp is null || totp.RetiredAt is not null ? CreateSetup(adminId, totp) : ReadSetup(totp);
        }, cancellationToken);

    /// <summary>Creates a new secret, replacing one a recovery retired; the retired secret is never shown again.</summary>
    private ErrorOr<AdminMfaSetup> CreateSetup(Guid adminId, AdminTotp? retired)
    {
        if (keys.ActiveKeyId is not { Length: > 0 } keyId) return KeyUnavailable;
        Span<byte> secret = stackalloc byte[AdminTotpAlgorithm.SecretBytes];
        RandomNumberGenerator.Fill(secret);
        try
        {
            if (retired is null) store.Add(keys.Encrypt(adminId, keyId, secret));
            else retired.Replace(keyId, keys.EncryptSecret(adminId, keyId, secret));
            return new AdminMfaSetup(AdminTotpAlgorithm.EncodeSecret(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private ErrorOr<AdminMfaSetup> ReadSetup(AdminTotp totp)
    {
        Span<byte> secret = stackalloc byte[AdminTotpAlgorithm.SecretBytes];
        try
        {
            if (!keys.TryDecrypt(totp, secret)) return KeyUnavailable;
            return new AdminMfaSetup(AdminTotpAlgorithm.EncodeSecret(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private Task<ErrorOr<T>> WithPendingAsync<T>(Guid adminId, string digest,
        Func<AdminIdentity, DateTimeOffset, Task<ErrorOr<T>>> operation, CancellationToken cancellationToken) =>
        store.WithAdminAsync<T>(adminId, async admin =>
        {
            var now = clock.GetUtcNow();
            if (admin.MfaEnrolledAt is not null || admin.DeactivatedAt is not null) return Refused;
            var enrollment = await bootstrap.FindEnrollmentAsync(digest, now, cancellationToken);
            if (enrollment?.OwnerId != adminId) return Refused;
            return await operation(admin, now);
        }, cancellationToken);

    public Task<ErrorOr<AdminMfaCompletion>> CompleteAsync(Guid adminId, string credentialDigest, string? otp,
        CancellationToken cancellationToken) =>
        WithPendingAsync<AdminMfaCompletion>(adminId, credentialDigest, async (admin, now) =>
        {
            var totp = await store.ReadTotpAsync(adminId, cancellationToken);
            if (totp is null || totp.RetiredAt is not null) return Refused;
            if (totp.TakeAttempt(now) is { } retryAt) return Throttled(retryAt);
            var result = VerifyStep(keys, totp, otp, now);
            if (result.IsError)
            {
                store.Audit(adminId, BootstrapAuditAction.MfaEnrollmentRefused, AuditReason.AuthenticatorProofInvalid, now);
                return result.Errors;
            }

            // Codes left over from before a recovery stop working once the new authenticator is verified.
            await store.VoidRecoveryCodesAsync(adminId, now, cancellationToken);
            var codes = IssueRecoveryCodes(totp);
            totp.AcceptStep(result.Value);
            admin.CompleteMfaEnrollment(now);
            await bootstrap.RevokeEnrollmentAsync(adminId, now, cancellationToken);
            store.Audit(adminId, BootstrapAuditAction.MfaEnrolled, AuditReason.AuthenticatorEnrolled, now,
                before: new(AuditTarget.Administrator, adminId, AuditState.MfaPending, MilestoneAt: admin.VerifiedAt),
                after: new(AuditTarget.Administrator, adminId, AuditState.MfaEnrolled, MilestoneAt: now));
            return new AdminMfaCompletion(codes);
        }, cancellationToken);

    internal static Error Throttled(DateTimeOffset retryAt) => Error.Custom(429, "Mfa.Throttled",
        "Too many attempts. Try again later.", new Dictionary<string, object> { ["retryAt"] = retryAt });

    internal static Error InvalidProof => Error.Validation("Mfa.InvalidProof", "Enter a current authenticator code.");

    internal static ErrorOr<long> VerifyStep(AdminMfaKeys keys, AdminTotp totp, string? otp, DateTimeOffset now)
    {
        Span<byte> secret = stackalloc byte[AdminTotpAlgorithm.SecretBytes];
        try
        {
            if (!keys.TryDecrypt(totp, secret)) return KeyUnavailable;
            return AdminTotpAlgorithm.Match(secret, otp, now, totp.LastAcceptedStep) is { } step ? step : InvalidProof;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private string[] IssueRecoveryCodes(AdminTotp totp)
    {
        var codes = new string[10];
        for (var i = 0; i < codes.Length; i++)
        {
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            store.Add(new AdminRecoveryCode(totp.AdminId, keys.HashRecovery(totp, raw)!));
            codes[i] = string.Join('-', raw[..8], raw[8..16], raw[16..24], raw[24..]);
        }
        return codes;
    }
}
