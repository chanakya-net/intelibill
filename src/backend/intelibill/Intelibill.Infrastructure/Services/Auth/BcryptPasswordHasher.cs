using Intelibill.Application.Common.Interfaces;
using Intelibill.Shared.Security;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class BcryptPasswordHasher(IPasswordHashing passwords) : IPasswordHasher
{
    public string Hash(string password) => passwords.Hash(password);

    public bool Verify(string password, string hash) => passwords.Verify(password, hash);
}
