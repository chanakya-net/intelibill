namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Turns a verification code into the value stored on a challenge. A keyed HMAC, not a digest: a
/// six-digit code has a million possibilities, so a plain hash column is a plaintext column with
/// extra steps for anyone holding a database dump.
/// </summary>
public interface IPhoneCodeHasher
{
    /// <summary>False when no key is configured, which must stop a code being issued at all.</summary>
    bool Available { get; }

    /// <summary>Identifies the key in use, recorded on each challenge so the key can be rotated.</summary>
    string KeyId { get; }

    string Hash(string code);

    /// <summary>
    /// Constant-time comparison against a stored hash. False when the hash was produced by a key
    /// that is no longer configured: the code cannot be checked, so it cannot pass.
    /// </summary>
    bool Matches(string code, string codeHash, string keyId);
}
