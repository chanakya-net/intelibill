using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal static class AdminTotpAlgorithm
{
    internal const int SecretBytes = 20;
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    internal static string EncodeSecret(ReadOnlySpan<byte> secret)
    {
        Span<char> encoded = stackalloc char[SecretBytes * 8 / 5];
        for (var i = 0; i < encoded.Length; i++)
        {
            var bit = i * 5;
            var index = bit / 8;
            var pair = (secret[index] << 8) | (index + 1 < secret.Length ? secret[index + 1] : 0);
            encoded[i] = Base32[(pair >> (11 - bit % 8)) & 31];
        }
        return new string(encoded);
    }

    internal static long? Match(ReadOnlySpan<byte> secret, string? otp, DateTimeOffset now, long? lastAccepted)
    {
        if (otp is not { Length: 6 } || !otp.All(char.IsAsciiDigit)) return null;
        Span<byte> presented = stackalloc byte[6];
        for (var i = 0; i < otp.Length; i++) presented[i] = (byte)otp[i];
        var current = now.ToUnixTimeSeconds() / 30;
        // Evaluate all three adjacent steps. A collision with a used step is a replay.
        long? match = null;
        var replay = false;
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step < 0 || !Matches(secret, presented, step)) continue;
            if (step <= lastAccepted) replay = true;
            else match = step;
        }
        return replay ? null : match;
    }

    private static bool Matches(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> presented, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        Span<byte> hash = stackalloc byte[20];
        Span<byte> expected = stackalloc byte[6];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        // RFC 4226 HOTP with SHA-1, as supported by standard authenticator apps.
#pragma warning disable CA5350
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash[(hash[^1] & 15)..]) & 0x7FFFFFFFu;
        Utf8Formatter.TryFormat(value % 1_000_000, expected, out _, new System.Buffers.StandardFormat('D', 6));
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
