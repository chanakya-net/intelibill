using System.Security.Cryptography;
using System.Text;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal static class WebhookSignature
{
    internal static bool Secret(string? value) => value is { Length: > 0 and <= 512 } && !string.IsNullOrWhiteSpace(value);

    internal static bool Identifier(string? value, int max) => value is { Length: > 0 } && value.Length <= max &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or '/');

    internal static bool Matches(ReadOnlySpan<byte> body, string? signature, IReadOnlyList<string> secrets)
    {
        Span<byte> supplied = stackalloc byte[32];
        if (signature is not { Length: 64 } ||
            Convert.FromHexString(signature, supplied, out _, out _) != System.Buffers.OperationStatus.Done)
            return false;

        Span<byte> expected = stackalloc byte[32];
        var matches = false;
        foreach (var secret in secrets)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            HMACSHA256.HashData(key, body, expected);
            CryptographicOperations.ZeroMemory(key);
            matches |= CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        return matches;
    }
}
