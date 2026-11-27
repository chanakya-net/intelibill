using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Intelibill.Application.Common.Operations;

/// <summary>
/// Hashes a command's canonical business payload. The payload names the target and the business
/// fields only — never transport metadata such as the key itself or headers.
/// </summary>
public static class FinancialOperationHasher
{
    /// <summary>Bump when a payload record changes shape; stored on every receipt.</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Compute<TPayload>(TPayload payload)
        where TPayload : notnull
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Drops trailing zeros so ₹500 and ₹500.00 hash alike.</summary>
    public static decimal NormalizeAmount(decimal amount) =>
        amount / 1.0000000000000000000000000000m;

    /// <summary>Trims and bounds a client-supplied key; null when absent or blank.</summary>
    public static string? NormalizeKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : key.Trim();
}
