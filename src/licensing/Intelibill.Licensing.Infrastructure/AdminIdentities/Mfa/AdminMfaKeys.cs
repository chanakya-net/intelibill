using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal sealed class AdminMfaOptions
{
    public string? ActiveKeyId { get; init; }
    public Dictionary<string, string> Keys { get; init; } = new(StringComparer.Ordinal);

    internal bool IsValid()
    {
        if (string.IsNullOrEmpty(ActiveKeyId) && Keys.Values.All(string.IsNullOrEmpty)) return true;
        return ActiveKeyId is not null && Keys.ContainsKey(ActiveKeyId)
            && Keys.All(key => key.Key.Length is > 0 and <= 32
                && key.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                && IsKey(key.Value));
    }

    private static bool IsKey(string value)
    {
        Span<byte> decoded = stackalloc byte[32];
        var valid = Convert.TryFromBase64String(value, decoded, out var count) && count == decoded.Length;
        CryptographicOperations.ZeroMemory(decoded);
        return valid;
    }
}

internal sealed class AdminMfaKeys(IOptions<AdminMfaOptions> options)
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    internal string? ActiveKeyId => options.Value.ActiveKeyId;

    internal AdminTotp Encrypt(Guid adminId, string keyId, ReadOnlySpan<byte> secret) =>
        new(adminId, keyId, EncryptSecret(adminId, keyId, secret));

    internal byte[] EncryptSecret(Guid adminId, string keyId, ReadOnlySpan<byte> secret)
    {
        var key = Derive(keyId, "intelibill-admin/totp-encryption/v1"u8)!;
        try
        {
            var payload = new byte[NonceBytes + TagBytes + secret.Length];
            RandomNumberGenerator.Fill(payload.AsSpan(0, NonceBytes));
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(payload.AsSpan(0, NonceBytes), secret, payload.AsSpan(NonceBytes + TagBytes),
                payload.AsSpan(NonceBytes, TagBytes), adminId.ToByteArray());
            return payload;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal bool TryDecrypt(AdminTotp totp, Span<byte> secret)
    {
        var key = Derive(totp.KeyId, "intelibill-admin/totp-encryption/v1"u8);
        if (key is null) return false;
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            var payload = totp.EncryptedSecret.AsSpan();
            aes.Decrypt(payload[..NonceBytes], payload[(NonceBytes + TagBytes)..],
                payload.Slice(NonceBytes, TagBytes), secret, totp.AdminId.ToByteArray());
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal string? HashRecovery(AdminTotp totp, string normalized)
    {
        var key = Derive(totp.RecoveryKeyId ?? totp.KeyId, "intelibill-admin/recovery-hmac/v1"u8);
        if (key is null) return null;
        try
        {
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{totp.AdminId:N}:{normalized}")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[]? Derive(string keyId, ReadOnlySpan<byte> purpose)
    {
        if (!options.Value.Keys.TryGetValue(keyId, out var encoded)) return null;
        var root = Convert.FromBase64String(encoded);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, info: purpose.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
        }
    }
}
