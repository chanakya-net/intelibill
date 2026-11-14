using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests
{
    [Fact]
    public async Task PersistentVerifier_RejectsEnrollmentStepAndConsumesLaterTotpAndRecoveryOnlyOnce()
    {
        var runtime = await NewDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = MfaSettings();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        await using var replica = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        using var browser = Client(api);
        await BootstrapAsync(browser, api);
        var adminId = (Guid)(await ReadFactAsync(runtime, "SELECT owner_id FROM initial_admin_bootstrap"))!;
        var csrf = await ReadMfaCsrfAsync(browser);
        var seed = await SetupSecretAsync(browser, csrf);
        var firstCode = AuthenticatorCode(seed, clock.GetUtcNow());
        await using var firstScope = api.Services.CreateAsyncScope();
        await using var secondScope = replica.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetService<IAdminMfaVerifier>();
        var second = secondScope.ServiceProvider.GetService<IAdminMfaVerifier>();
        Assert.True(first is not null && second is not null, "The persistent MFA verifier must be available for later sign-in and recovery.");
        Assert.True((await first!.ConsumeTotpAsync(adminId, firstCode, CancellationToken.None)).IsError);

        using var completed = await VerifyAsync(browser, csrf, firstCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var result = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
        var recoveryCode = result.RootElement.GetProperty("recoveryCodes")[0].GetString()!;
        await using var completedScope = api.Services.CreateAsyncScope();
        var verifierAfterEnrollment = completedScope.ServiceProvider.GetRequiredService<IAdminMfaVerifier>();
        var replay = await verifierAfterEnrollment.ConsumeTotpAsync(adminId, firstCode, CancellationToken.None);
        Assert.True(replay.IsError && replay.FirstError.Code == "Mfa.InvalidProof");

        clock.Advance(TimeSpan.FromSeconds(30));
        var nextCode = AuthenticatorCode(seed, clock.GetUtcNow());
        Assert.True((await second!.ConsumeTotpAsync(adminId, firstCode, CancellationToken.None)).IsError);
        // Reuse the scope that previously loaded the older counter, racing two hosts.
        await AssertSingleUseAsync(verifierAfterEnrollment, second,
            verifier => verifier.ConsumeTotpAsync(adminId, nextCode, CancellationToken.None));
        await AssertSingleUseAsync(verifierAfterEnrollment, second,
            verifier => verifier.ConsumeRecoveryCodeAsync(adminId, recoveryCode, CancellationToken.None));
        Assert.True((await verifierAfterEnrollment.ConsumeTotpAsync(adminId, nextCode, CancellationToken.None)).IsError);
        Assert.Equal(1L, await ReadFactAsync(runtime, "SELECT count(*) FROM admin_recovery_codes WHERE consumed_at IS NOT NULL"));
    }

    private static async Task AssertSingleUseAsync(IAdminMfaVerifier first, IAdminMfaVerifier second,
        Func<IAdminMfaVerifier, Task<ErrorOr.ErrorOr<AdminMfaProof>>> consume)
    {
        var results = await Task.WhenAll(consume(first), consume(second));
        Assert.Equal(1, results.Count(result => !result.IsError));
    }
}
