using System.Text.Json;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests
{
    [Fact]
    public void ShippingEmailCopyMatchesEveryMerchantLocaleAndKeepsOperationalGuidanceSeparate()
    {
        var assembly = typeof(LicensingDbContext).Assembly;
        foreach (var locale in LicenseNoticesV1.SupportedLocales)
        {
            using var input = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.Notifications.Templates.{locale}.json");
            Assert.NotNull(input);
            using var email = JsonDocument.Parse(input);
            var merchantPath = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill/public/assets/i18n", locale + ".json");
            using var merchant = JsonDocument.Parse(File.ReadAllText(merchantPath));
            var notices = merchant.RootElement.GetProperty("billing").GetProperty("notices");
            foreach (var name in new[] { "effectiveDeadline", "purposes", "thresholds", "guidance", "operationalGuidance",
                         "evidencedTermPrice", "renewalQuoteNote" })
                Assert.True(JsonElement.DeepEquals(notices.GetProperty(name), email.RootElement.GetProperty(name)), locale + " " + name);
            foreach (var field in email.RootElement.GetProperty("proof").EnumerateObject())
                Assert.False(string.IsNullOrWhiteSpace(field.Value.GetString()));
        }
    }

    [Theory]
    [InlineData("BillingEmail:FromEmail", "unverified@company.com")]
    [InlineData("BillingEmail:SupportEmail", "support@company.com")]
    [InlineData("BillingEmail:VerificationOrigin", "https://office.example.test")]
    [InlineData("AdminMail:LocalCapture", "false")]
    public async Task InvalidCaptureConfigurationLeavesProofPending(string key, string value)
    {
        var options = mailpit.BillingSettings();
        options[key] = value;
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: options);
        var contact = await ApproveStandaloneAsync(fixture);
        Assert.False(await DeliverAsync(fixture));
        Assert.Equal("Pending", await ProofStateAsync(fixture, contact));
    }
}
