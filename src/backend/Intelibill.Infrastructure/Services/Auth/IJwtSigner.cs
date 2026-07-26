namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Produces the signature over a JWT's <c>header.payload</c>.
/// </summary>
/// <remarks>
/// Asynchronous because one implementation signs over the network. That rules
/// out <c>JwtSecurityTokenHandler</c>, whose signing path is synchronous — a
/// Key Vault call behind it would have to block a request thread on I/O. The
/// token is therefore assembled by hand in <see cref="TokenService"/>, which is
/// a few lines of Base64Url and gives both modes one code path.
/// </remarks>
internal interface IJwtSigner
{
    /// <summary>JWS algorithm name for the <c>alg</c> header, e.g. HS256.</summary>
    string Algorithm { get; }

    /// <summary>
    /// The <c>kid</c> to advertise, resolved before signing because it is part of
    /// the header and therefore of the signed input. Null when there is nothing
    /// to identify, as with a single shared HMAC secret.
    /// </summary>
    ValueTask<string?> GetKeyIdAsync(CancellationToken cancellationToken = default);

    ValueTask<byte[]> SignAsync(byte[] signingInput, CancellationToken cancellationToken = default);
}
