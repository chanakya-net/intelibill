using System.Security.Cryptography;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Shared.Security;

namespace Intelibill.Licensing.Infrastructure.Bootstrap;

internal sealed class BootstrapSecrets(OpaqueTokenPrimitives tokens, IPasswordHashing passwords) : IBootstrapSecrets
{
    public BootstrapSecret CreateProof() => Create("p1_");

    public BootstrapSecret CreateEnrollment() => Create("e1_");

    public bool MatchesProof(string value, string digest) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(tokens.Hash(value)), Convert.FromHexString(digest));

    public string HashEnrollment(string credential) => tokens.Hash(credential);

    public string HashPassword(string password) => passwords.Hash(password);

    public bool VerifyPassword(string password, string hash) => passwords.Verify(password, hash);

    private BootstrapSecret Create(string prefix)
    {
        var value = prefix + tokens.NewToken();
        return new BootstrapSecret(value, tokens.Hash(value));
    }
}
