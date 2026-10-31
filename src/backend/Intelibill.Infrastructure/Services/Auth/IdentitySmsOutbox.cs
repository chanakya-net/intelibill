using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Renders and encrypts an identity SMS into the caller's transaction. Uses the same AES-GCM
/// construction and the same <c>Identity:OutboxKey</c> as the mail outbox, with the destination as
/// associated data, so one key rotation covers both and a row cannot be replayed against a
/// different number.
/// </summary>
internal sealed class IdentitySmsOutbox(
    IPhoneChallengeRepository repository,
    IConfiguration configuration,
    IOptions<PhoneAuthOptions> options) : IIdentitySmsOutbox
{
    public bool Enabled => options.Value.Enabled && TryKey(out _);

    public void QueueCode(Guid challengeId, string destination, string purpose, string code, DateTimeOffset expiresAt)
    {
        // Plain text, no link. The international route into India rejects shortened URLs, and a
        // code the holder has to read out of a link is worse than one they can read.
        var minutes = Math.Max(1, options.Value.CodeLifetimeMinutes);
        var action = purpose switch
        {
            PhoneChallengePurposes.Login => "sign in to Intelibill",
            PhoneChallengePurposes.PhoneChange => "confirm this number on your Intelibill account",
            _ => "verify this number for Intelibill",
        };

        var body = $"{code} is your Intelibill code to {action}. It expires in {minutes} minutes. Do not share it with anyone.";
        Queue(challengeId, destination, body, expiresAt);
    }

    public void QueueSecurityNotice(string destination, bool bound) => Queue(
        null,
        destination,
        bound
            ? "This number is now the sign-in number for an Intelibill account. Existing devices have been signed out. If this was not you, contact support immediately."
            : "This number is no longer the sign-in number for an Intelibill account. If this was not you, contact support immediately.",
        DateTimeOffset.UtcNow.AddDays(1));

    private void Queue(Guid? challengeId, string destination, string body, DateTimeOffset expiresAt) =>
        repository.AddSms(IdentitySms.Create(challengeId, destination, Encrypt(destination, body), expiresAt));

    private string Encrypt(string destination, string body)
    {
        if (!TryKey(out var key))
            throw new InvalidOperationException("Identity:OutboxKey must contain a base64-encoded 32-byte key.");

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new IdentitySmsContent(body));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes(destination));
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    internal IdentitySmsContent Decrypt(IdentitySms message)
    {
        if (!TryKey(out var key))
            throw new InvalidOperationException("Identity SMS encryption key is unavailable.");

        var data = Convert.FromBase64String(message.EncryptedContent);
        var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(message.Destination));
        try
        {
            return JsonSerializer.Deserialize<IdentitySmsContent>(plain)
                ?? throw new InvalidOperationException("Invalid identity SMS payload.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private bool TryKey(out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(configuration["Identity:OutboxKey"] ?? string.Empty);
            return key.Length == 32;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}

internal sealed record IdentitySmsContent(string Body);
