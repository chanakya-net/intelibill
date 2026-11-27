using Intelibill.Shared.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BC = BCrypt.Net.BCrypt;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// BCrypt at the minimum work factor for test hosts. Same <c>$2a$</c> format and 60-character hashes as
/// production, so stored rows look alike, but each hash or verify costs about 1 ms instead of about 250 ms.
/// Fixtures sign in several merchants and admins per test, which made cost-12 hashing a large share of
/// every test. Production's cost-12 contract stays covered by the shared consumer regression tests.
/// </summary>
internal sealed class FastPasswordHashing : IPasswordHashing
{
    public string Hash(string password) => BC.HashPassword(password, BC.GenerateSalt(4));

    public bool Verify(string password, string hash) => BC.Verify(password, hash);

    internal static void Use(IServiceCollection services)
    {
        services.RemoveAll<IPasswordHashing>();
        services.AddSingleton<IPasswordHashing, FastPasswordHashing>();
    }
}
