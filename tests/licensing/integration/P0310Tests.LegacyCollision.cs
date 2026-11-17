using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task RetainedProfileCannotOverrideLegacyCredentialsUnderTheSamePin()
    {
        var settings = RazorpayProfiles("acc_b0310", "Test", "b/1", "rzp_test_a0310", "rzp_test_b0310");
        foreach (var (key, value) in FakeRazorpay.Settings())
            if (key.StartsWith("Payments:Razorpay:", StringComparison.Ordinal)) settings[key] = value;
        settings["Payments:Razorpay:AccountId"] = "acc_a0310";
        settings["Payments:Razorpay:ConfigurationVersion"] = "a/1";
        settings["Payments:Razorpay:KeyId"] = "rzp_test_legacy0310";
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, settings);
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();
        Assert.Null(resolver.ForPin(new("razorpay", "acc_a0310", "Test", "a/1")));
        Assert.NotNull(resolver.ForPin(new("razorpay", "acc_b0310", "Test", "b/1")));
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));
    }
}

public sealed partial class P0310Tests
{
    [Fact]
    public async Task MissingRetainedProfileNeverFallsBackToLegacyEvenWhenItsPinMatches()
    {
        var settings = RazorpayProfiles("acc_b0310", "Test", "b/1", "rzp_test_a0310", "rzp_test_b0310");
        const string prefix = "FixtureCredentials:0:";
        foreach (var (key, value) in settings.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            settings["Payments:Razorpay:" + key[prefix.Length..]] = value;
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, settings);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var config = fixture.Hosts.Licensing.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        config["Payments:Profiles:0:ProviderId"] = "not-loaded";
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();
        Assert.Null(resolver.ForPin(new("razorpay", "acc_a0310", "Test", "a/1")));
        Assert.NotNull(resolver.ForPin(new("razorpay", "acc_b0310", "Test", "b/1")));
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));
    }
}
