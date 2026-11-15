namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal sealed class AdminTotp(Guid adminId, string keyId, byte[] encryptedSecret)
{
    internal const int AttemptLimit = 5;
    internal static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    public Guid AdminId { get; private set; } = adminId;
    public string KeyId { get; private set; } = keyId;
    public string? RecoveryKeyId { get; private set; }
    public byte[] EncryptedSecret { get; private set; } = encryptedSecret;
    public long? LastAcceptedStep { get; private set; }
    public DateTimeOffset? AttemptWindowEndsAt { get; private set; }
    public int Attempts { get; private set; }
    /// <summary>Set by a recovery: the secret can no longer be shown or verified and enrollment replaces it.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    internal void Retire(DateTimeOffset now)
    {
        RetiredAt ??= now;
        // Erase the ciphertext as well as marking it retired, including for predecessor readers.
        EncryptedSecret = new byte[EncryptedSecret.Length];
    }

    internal void Replace(string keyId, byte[] encryptedSecret)
    {
        RecoveryKeyId ??= KeyId;
        KeyId = keyId;
        EncryptedSecret = encryptedSecret;
        LastAcceptedStep = null;
        RetiredAt = null;
        ResetAttempts();
    }

    internal void UseSetupKeyForRecoveryCodes() => RecoveryKeyId = null;

    internal DateTimeOffset? TakeAttempt(DateTimeOffset now)
    {
        if (AttemptWindowEndsAt is null || AttemptWindowEndsAt <= now)
        {
            AttemptWindowEndsAt = now.Add(AttemptWindow);
            Attempts = 0;
        }

        if (Attempts >= AttemptLimit) return AttemptWindowEndsAt;
        Attempts++;
        return null;
    }

    internal void AcceptStep(long step)
    {
        LastAcceptedStep = step;
        ResetAttempts();
    }

    internal void ResetAttempts()
    {
        Attempts = 0;
        AttemptWindowEndsAt = null;
    }
}

internal sealed class AdminRecoveryCode(Guid adminId, string digest)
{
    public Guid AdminId { get; private set; } = adminId;
    public string Digest { get; private set; } = digest;
    public DateTimeOffset? ConsumedAt { get; private set; }

    internal void Consume(DateTimeOffset now) => ConsumedAt = now;
}
