namespace Intelibill.Licensing.Domain.Entities;

public sealed class AdminIdentity(
    Guid id, string email, string passwordHash, string verificationReference, DateTimeOffset verifiedAt,
    AdminCapability capabilities = AdminCapability.None)
{
    public Guid Id { get; private set; } = id;
    public string Email { get; private set; } = email;
    public string PasswordHash { get; private set; } = passwordHash;
    public string VerificationReference { get; private set; } = verificationReference;
    public DateTimeOffset VerifiedAt { get; private set; } = verifiedAt;

    public DateTimeOffset? MfaEnrolledAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public string? RecoveryContinuationDigest { get; private set; }
    public AdminCapability Capabilities { get; private set; } = capabilities;
    public bool IsPlatformOwner => Capabilities.HasFlag(AdminCapability.PlatformOwner);
    public uint Version { get; private set; }

    public void UpdateCapabilities(AdminCapability grants, bool disable, DateTimeOffset now)
    {
        Capabilities = grants;
        if (disable) DeactivatedAt ??= now;
    }

    public void CompleteMfaEnrollment(DateTimeOffset now)
    {
        MfaEnrolledAt ??= now;
        RecoveryContinuationDigest = null;
    }

    public void SaveRecoveryContinuation(string digest) => RecoveryContinuationDigest = digest;

    /// <summary>Returns the administrator to MFA-pending; only a new enrollment restores business access.</summary>
    public void ResetMfa()
    {
        MfaEnrolledAt = null;
        RecoveryContinuationDigest = null;
    }
}
