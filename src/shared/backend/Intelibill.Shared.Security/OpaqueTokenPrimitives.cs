using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Shared.Security;

/// <summary>Generates high-entropy opaque credentials and hashes their exact UTF-8 representation for storage.</summary>
public sealed class OpaqueTokenPrimitives
{
    private const int SecretBytes = 64;

    public OpaqueTokenV1 Create()
    {
        var token = NewToken();
        return new OpaqueTokenV1(token, Hash(token));
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Part of the instance API registered by both applications.")]
    public string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Part of the instance API registered by both applications.")]
    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
