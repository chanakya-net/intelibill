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
/// <para>
/// Without a configured key the material is hashed with plain SHA-256 instead. Every replica still
/// agrees, so limits hold across the service; what is lost is that a leaked table can be checked
/// against guessed addresses. That is logged as an error outside Development and Testing. A key
/// generated per process would avoid the error and break the limit: each replica would count
/// the same caller in a different bucket.
/// </para>
/// <para>
/// Moving from unkeyed to keyed is a rotation like any other: set the new
/// <see cref="RateLimitingOptions.BucketKey"/> with <see cref="RateLimitingOptions.PreviousBucketKey"/>
/// set to <see cref="UnkeyedSentinel"/>, so the buckets counted so far keep counting for the
/// overlap instead of every allowance starting again at once.
/// </para>
/// </summary>
public sealed partial class RateLimitKeyHasher
{
    /// <summary>The <see cref="RateLimitingOptions.PreviousBucketKey"/> value that names unkeyed hashing.</summary>
    public const string UnkeyedSentinel = "unkeyed";

    private static readonly byte[] UnkeyedLabel = Encoding.UTF8.GetBytes("intelibill.rate-limit.unkeyed.v1\n");

    // An empty entry stands for unkeyed hashing.
    private readonly byte[][] _keys;

    public RateLimitKeyHasher(
        IOptions<RateLimitingOptions> options,
        IHostEnvironment? environment = null,
        ILogger<RateLimitKeyHasher>? logger = null)
    {
        var value = options.Value;
        if (!TryParseKey(value.BucketKey, out var current))
        {
            _keys = [[]];
            if (logger is not null && environment is not null && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                LogUnkeyed(logger);
            return;
        }

        if (IsUnkeyedSentinel(value.PreviousBucketKey))
            _keys = [current, []];
        else
            _keys = TryParseKey(value.PreviousBucketKey, out var previous) ? [current, previous] : [current];
    }

    /// <summary>One key per configured secret, the current secret first.</summary>
    public IReadOnlyList<string> Hash(string material)
    {
        var bytes = Encoding.UTF8.GetBytes(material);
        var hashes = new string[_keys.Length];
        for (var i = 0; i < _keys.Length; i++)
        {
            hashes[i] = _keys[i].Length == 0
                ? Convert.ToHexStringLower(SHA256.HashData([.. UnkeyedLabel, .. bytes]))
                : Convert.ToHexStringLower(HMACSHA256.HashData(_keys[i], bytes));
        }

        return hashes;
    }

    internal static bool IsUnkeyedSentinel(string? value) =>
        string.Equals(value?.Trim(), UnkeyedSentinel, StringComparison.OrdinalIgnoreCase);

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

    // Error, not Warning: deployed environments log Error and above, and this is a protection
    // running below its intended strength rather than a passing condition.
    [LoggerMessage(Level = LogLevel.Error, Message = "RateLimiting:BucketKey is not configured; rate-limit buckets are hashed without a key. Limits still hold across replicas, but supply a base64 32-byte key from the secret store.")]
    private static partial void LogUnkeyed(ILogger logger);
}
