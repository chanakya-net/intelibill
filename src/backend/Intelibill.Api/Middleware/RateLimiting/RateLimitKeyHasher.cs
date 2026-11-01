using System.Security.Cryptography;
using System.Text;
using Intelibill.Api.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Middleware.RateLimiting;

/// <summary>
/// Turns bucket material — an address, an account, a destination — into the opaque key the store
/// holds: HMAC-SHA256 under <see cref="RateLimitingOptions.BucketKey"/>, in lower-case hex. A key
/// is useless to anyone reading the table without the secret, and two replicas with the same
/// secret agree on every key.
/// </summary>
public sealed class RateLimitKeyHasher
{
    private readonly byte[][] _keys;

    public RateLimitKeyHasher(IOptions<RateLimitingOptions> options)
    {
        var value = options.Value;

        // The validator refuses a missing key outside Development and Testing. There, a key
        // generated per process is enough: one replica, and buckets that reset on restart.
        var current = TryParseKey(value.BucketKey, out var parsed) ? parsed : RandomNumberGenerator.GetBytes(32);
        _keys = TryParseKey(value.PreviousBucketKey, out var previous) ? [current, previous] : [current];
    }

    /// <summary>One key per configured secret, the current secret first.</summary>
    public IReadOnlyList<string> Hash(string material)
    {
        var bytes = Encoding.UTF8.GetBytes(material);
        var hashes = new string[_keys.Length];
        for (var i = 0; i < _keys.Length; i++)
            hashes[i] = Convert.ToHexStringLower(HMACSHA256.HashData(_keys[i], bytes));
        return hashes;
    }

    internal static bool TryParseKey(string? value, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            key = Convert.FromBase64String(value.Trim());
            return key.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
