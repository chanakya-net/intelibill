using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class MerchantBillingDelegationKeysTests
{
    private const string Vault = "https://billing-vault.test";
    private const string KeyName = "billing";
    private const string Key = Vault + "/keys/" + KeyName;
    private readonly BillingOptions _options = new();
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();
    private readonly KeyClient _vault = Substitute.For<KeyClient>();
    private readonly BillingClock _clock = new();

    [Fact]
    public async Task PinnedVersion_StaysSelectedUntilConfigurationChanges()
    {
        SetReference(Key + "/old");
        var old = StubSigner("old");
        var next = StubSigner("next");
        var clientCreations = 0;
        using var keys = new MerchantBillingDelegationKeys(_options, OptionsFactory.Create(new JwtOptions()), _environment, _clock,
            (_, _) => { clientCreations++; return _vault; });

        using var original = await keys.ResolveAsync(TestContextToken);
        Assert.NotNull(original);
        Assert.Equal("old", original.Id);
        using var unchanged = await keys.ResolveAsync(TestContextToken);
        Assert.Equal("old", unchanged?.Id);
        Assert.Equal(1, clientCreations);

        SetReference(Key + "/next");
        using var rotated = await keys.ResolveAsync(TestContextToken);
        Assert.NotNull(rotated);
        Assert.Equal("next", rotated.Id);
        Assert.Equal(2, clientCreations);
        await original.SignAsync([1], TestContextToken);
        await rotated.SignAsync([2], TestContextToken);
        await old.Received(1).SignDataAsync(SignatureAlgorithm.RS256, Arg.Is<byte[]>(bytes => bytes[0] == 1), TestContextToken);
        await next.Received(1).SignDataAsync(SignatureAlgorithm.RS256, Arg.Is<byte[]>(bytes => bytes[0] == 2), TestContextToken);
        await _vault.DidNotReceive().GetKeyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VersionlessReference_CachesTheVersionAndRefreshesWithTheSameVaultClient()
    {
        SetReference(Key);
        StubSigner("old");
        StubSigner("next");
        StubCurrentVersion("old");
        var clientCreations = 0;
        using var keys = new MerchantBillingDelegationKeys(_options, OptionsFactory.Create(new JwtOptions()), _environment, _clock,
            (_, _) => { clientCreations++; return _vault; });

        using var first = await keys.ResolveAsync(TestContextToken);
        Assert.Equal("old", first?.Id);
        StubCurrentVersion("next");
        _clock.Now = _clock.Now.AddMinutes(5).AddMilliseconds(-1);
        using var cached = await keys.ResolveAsync(TestContextToken);
        Assert.Same(first, cached);
        await _vault.Received(1).GetKeyAsync(KeyName, cancellationToken: TestContextToken);

        _clock.Now = _clock.Now.AddMilliseconds(1);
        using var refreshed = await keys.ResolveAsync(TestContextToken);
        Assert.Equal("next", refreshed?.Id);
        await _vault.Received(2).GetKeyAsync(KeyName, cancellationToken: TestContextToken);
        Assert.Equal(1, clientCreations);
    }

    [Theory]
    [InlineData(Key, Vault + "/keys/BILLING/old/")]
    [InlineData(Key + "/next", Key)]
    [InlineData(Key + "/next", Key + "/")]
    [InlineData(Key + "/next", Key + "/old")]
    [InlineData("http://billing-vault.test/keys/billing/next", null)]
    [InlineData("not a key URI", null)]
    [InlineData(Key + "/next/extra", null)]
    public async Task InvalidOrSharedReference_DoesNotConstructAVaultClient(string reference, string? jwtKey)
    {
        SetReference(reference);
        using var keys = new MerchantBillingDelegationKeys(_options, OptionsFactory.Create(new JwtOptions { KeyVaultKeyId = jwtKey }),
            _environment, _clock, (_, _) => throw new InvalidOperationException("Unexpected test vault client creation."));

        Assert.Null(await keys.ResolveAsync(TestContextToken));
    }

    private void SetReference(string reference) =>
        _options.CurrentValue = new MerchantBillingDelegationOptions { KeyVaultKeyId = reference };

    private CryptographyClient StubSigner(string version)
    {
        var signer = Substitute.For<CryptographyClient>();
        signer.SignDataAsync(SignatureAlgorithm.RS256, Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(CryptographyModelFactory.SignResult(Key + "/" + version, [1], SignatureAlgorithm.RS256));
        _vault.GetCryptographyClient(KeyName, version).Returns(signer);
        return signer;
    }

    private void StubCurrentVersion(string version)
    {
        var properties = KeyModelFactory.KeyProperties(new Uri(Key + "/" + version), new Uri(Vault), KeyName,
            version, true, null, null, null, null);
        var key = KeyModelFactory.KeyVaultKey(properties, new JsonWebKey([]));
        _vault.GetKeyAsync(KeyName, cancellationToken: TestContextToken)
            .Returns(Response.FromValue(key, Substitute.For<Response>()));
    }

    private static CancellationToken TestContextToken => CancellationToken.None;

    private sealed class BillingOptions : IOptionsMonitor<MerchantBillingDelegationOptions>
    {
        public MerchantBillingDelegationOptions CurrentValue { get; set; } = new();
        public MerchantBillingDelegationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<MerchantBillingDelegationOptions, string?> listener) => null;
    }

    private sealed class BillingClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
