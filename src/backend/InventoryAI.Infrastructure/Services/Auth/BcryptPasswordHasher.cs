using InventoryAI.Application.Common.Interfaces;
using BC = BCrypt.Net.BCrypt;

namespace InventoryAI.Infrastructure.Services.Auth;

internal sealed class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) =>
        BC.HashPassword(password, BC.GenerateSalt(12));

    public bool Verify(string password, string hash) =>
        BC.Verify(password, hash);
}
