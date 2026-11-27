using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Intelibill.Application.Features.Auth.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.P0103ConsumerRegressionTests;

[Collection(SharedHostsGroup.Name)]
public sealed partial class ConsumerTests(SharedPostgreSqlFixture database)
{
    [Fact]
    public async Task ExistingMerchantPasswordAndBothConsumersRemainCompatible()
    {
        var merchant = await database.SeedMerchantAsync();
        using var merchantHost = new MerchantHost(database.MerchantRuntime);
        using var licensingHost = new LicensingHost(database.LicensingRuntime);
        using var client = SecurityFixtures.HttpsClient(merchantHost);

        var auth = await LoginAsync(client, merchant.Email!);
        using var invalidLogin = await LoginRequestAsync(client, merchant.Email!, "wrong-fixture-password");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);
        Assert.NotNull(auth.SessionId);
        Assert.Equal(HttpStatusCode.OK, await ProtectedStatusAsync(client, auth.AccessToken));

        foreach (var services in new[] { merchantHost.Services, licensingHost.Services })
        {
            var hasher = Assert.IsType<BcryptPasswordHashing>(services.GetRequiredService<IPasswordHashing>());
            Assert.True(hasher.Verify(SecurityFixtures.Password, SecurityFixtures.PasswordHash));
            var hash = hasher.Hash(SecurityFixtures.Password);
            Assert.True(hash.StartsWith("$2a$12$", StringComparison.Ordinal), "New hashes use the existing cost-12 format.");
            Assert.False(string.Equals(hash, hasher.Hash(SecurityFixtures.Password), StringComparison.Ordinal), "Hashes use unique salts.");
            Assert.True(hasher.Verify(SecurityFixtures.Password, hash));
        }
    }

    [Fact]
    public void BcryptPreservesUnicodeAndLongInputAndMalformedHashBehavior()
    {
        const string legacyPassword = "P0103-mercearia-é-नमस्ते-fôr-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string legacyHash = "$2y$12$7DNpK/pZ5imv9W5R8HL7TeW724mzRgwMNWPexKKca7F9w2ZFvRiv2";
        var hasher = new BcryptPasswordHashing();
        Assert.True(hasher.Verify(legacyPassword, legacyHash));
        Assert.Throws<BCrypt.Net.SaltParseException>(() => hasher.Verify(SecurityFixtures.Password, "not-a-bcrypt-hash"));
    }

    [Fact]
    public void BothProductionHostsResolveTheOpaqueTokenPrimitive()
    {
        using var merchantHost = new MerchantHost(database.MerchantRuntime);
        using var licensingHost = new LicensingHost(database.LicensingRuntime);
        foreach (var services in new[] { merchantHost.Services, licensingHost.Services })
        {
            var tokens = services.GetRequiredService<OpaqueTokenPrimitives>();
            var credential = tokens.Create();
            Assert.Equal(86, credential.Token.Length);
            Assert.True(credential.Token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
            Assert.Equal(64, Base64Url.DecodeFromChars(credential.Token).Length);
            Assert.Matches("^[0-9A-F]{64}$", credential.TokenHash);
            Assert.True(string.Equals(tokens.Hash(credential.Token), credential.TokenHash, StringComparison.Ordinal), "The generated digest matches the exact token.");
            Assert.False(string.Equals(credential.Token, tokens.NewToken(), StringComparison.Ordinal));
            Assert.Equal(SecurityFixtures.LegacyHash, tokens.Hash(SecurityFixtures.LegacyToken));
            Assert.Equal("C87586D05F3A95A435B1B3DDE6831F819BF198D833F59770E886E629AC58DEED",
                tokens.Hash("  p0103-fixture-é  "));
            Assert.Throws<ArgumentNullException>(() => tokens.Hash(null!));
        }
    }

    [Fact]
    public void OpaqueTokenV1KeepsItsWireShapeAndRedactsItsDiagnosticString()
    {
        var value = new OpaqueTokenV1("fixture-token", "fixture-digest");
        const string expected = """{"token":"fixture-token","tokenHash":"fixture-digest"}""";
        Assert.Equal(expected, JsonSerializer.Serialize(value));
        Assert.Equal(value, JsonSerializer.Deserialize<OpaqueTokenV1>(expected));
        Assert.Equal(nameof(OpaqueTokenV1), value.ToString());
    }

    [Fact]
    public void MerchantPasswordAdapterDelegatesToTheSharedHasherRegistration()
    {
        using var merchantHost = new MerchantHost(database.MerchantRuntime);
        using var decorated = merchantHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHashing>();
            services.AddSingleton<IPasswordHashing, PasswordProbe>();
        }));
        using var scope = decorated.Services.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Assert.Equal("shared-probe-hash", adapter.Hash("probe-password"));
        Assert.True(adapter.Verify("probe-password", "shared-probe-hash"));
    }

    private sealed class PasswordProbe : IPasswordHashing
    {
        public string Hash(string password) =>
            password == "probe-password" ? "shared-probe-hash" : throw new InvalidOperationException();

        public bool Verify(string password, string hash) => password == "probe-password" && hash == "shared-probe-hash";
    }

    private static Task<HttpResponseMessage> LoginRequestAsync(HttpClient client, string identifier, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { identifier, password });

    private static async Task<AuthResult> LoginAsync(HttpClient client, string email)
    {
        using var response = await LoginRequestAsync(client, email, SecurityFixtures.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static async Task<HttpStatusCode> ProtectedStatusAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
