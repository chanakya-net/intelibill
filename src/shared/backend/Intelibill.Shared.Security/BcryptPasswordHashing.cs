using BC = BCrypt.Net.BCrypt;

namespace Intelibill.Shared.Security;

public sealed class BcryptPasswordHashing : IPasswordHashing
{
    public string Hash(string password) => BC.HashPassword(password, BC.GenerateSalt(12));

    public bool Verify(string password, string hash) => BC.Verify(password, hash);
}
