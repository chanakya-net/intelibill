using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Unit.Tests.Options;

public class JwtOptionsTests
{
    private static readonly JwtOptionsValidator Validator = new();

    private static ValidateOptionsResult Validate(JwtOptions options)
        => Validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

    [Fact]
    public void SigningModeDefaultsToHmac_SoATestOrDeveloperMachineNeedsNoAzure()
    {
        Assert.Equal(JwtSigningMode.Hmac, new JwtOptions().SigningMode);
    }

    [Fact]
    public void Validator_AcceptsHmacWithASufficientSecret()
    {
        var result = Validate(new JwtOptions
        {
            Issuer = "Intelibill",
            Audience = "Intelibill",
            Secret = new string('k', 32),
        });

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validator_RejectsHmacWithoutASecret()
    {
        var result = Validate(new JwtOptions { Issuer = "Intelibill", Audience = "Intelibill" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Jwt:Secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsAnHmacSecretShorterThanTheHash()
    {
        var result = Validate(new JwtOptions
        {
            Issuer = "Intelibill",
            Audience = "Intelibill",
            Secret = "too-short",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("at least 32", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsKeyVaultWithAKeyIdentifier()
    {
        var result = Validate(new JwtOptions
        {
            Issuer = "Intelibill",
            Audience = "Intelibill",
            SigningMode = JwtSigningMode.KeyVault,
            KeyVaultKeyId = "https://intelibill-prod-kv.vault.azure.net/keys/jwt-signing",
        });

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validator_RejectsKeyVaultWithoutAKeyIdentifier()
    {
        var result = Validate(new JwtOptions
        {
            Issuer = "Intelibill",
            Audience = "Intelibill",
            SigningMode = JwtSigningMode.KeyVault,
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Jwt:KeyVaultKeyId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsAKeyIdentifierThatIsNotAUri()
    {
        var result = Validate(new JwtOptions
        {
            Issuer = "Intelibill",
            Audience = "Intelibill",
            SigningMode = JwtSigningMode.KeyVault,
            KeyVaultKeyId = "jwt-signing",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("absolute key identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsKeyVaultModeCarryingALeftoverSecret()
    {
        // The failure this exists for: a deployed environment inheriting a
        // development secret and signing with it instead of the vault key.
        var result = Validate(new JwtOptions
        {
            Issuer = "Intelibill",
            Audience = "Intelibill",
            SigningMode = JwtSigningMode.KeyVault,
            KeyVaultKeyId = "https://intelibill-prod-kv.vault.azure.net/keys/jwt-signing",
            Secret = new string('k', 32),
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Jwt:SigningMode", StringComparison.Ordinal));
    }
}
