using System.Security.Cryptography;
using System.Text;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// HS256 with the configured shared secret. Local development and tests only —
/// verification uses the same value that signs, so possession of it is
/// possession of every identity the system can issue.
/// </summary>
internal sealed class HmacJwtSigner : IJwtSigner
{
    private readonly byte[] _key;

    public HmacJwtSigner(IOptions<JwtOptions> options)
    {
        var secret = options.Value.Secret
            ?? throw new InvalidOperationException("Jwt:Secret is required for HMAC signing.");

        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string Algorithm => "HS256";

    public ValueTask<string?> GetKeyIdAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(null);

    public ValueTask<byte[]> SignAsync(byte[] signingInput, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(HMACSHA256.HashData(_key, signingInput));
}
