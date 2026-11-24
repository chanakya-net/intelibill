using System.Diagnostics;
using Azure;
using Azure.Identity;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Licensing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    /// <summary>
    /// Exercises delayed key resolution, provider exceptions (resolution and signing), an unusable
    /// public-only local key and caller cancellation through the real OfflinePermitSigningKeys seam.
    /// Verifies bounded SigningUnavailable refusal, unchanged evidence and prompt release of PostgreSQL locks.
    /// </summary>
    [Fact]
    public async Task Signer_bounds_resolution_maps_provider_and_local_key_failures_and_releases_locks()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var shortBudget = TimeSpan.FromMilliseconds(250);
        Actor actor;
        Guid lease, grant;

        // 1. Seed shop, grant and completed snapshot with a healthy signer first.
        using (var setupHost = CreateHost())
        {
            await setupHost.InitializeAsync();
            using var client = CreateClient(setupHost);
            actor = await OpenShopAsync(client, verified, verified.AddDays(30));
            _clock.Now = verified.AddHours(1);
            (lease, var captured) = await ReserveAsync(setupHost, actor);
            grant = Issued(captured).GrantId;
        }

        // 2. Delayed resolver: bounded by the single resolution+signing budget; locks released promptly.
        var delayedKeys = new FaultySigningKeys(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        });
        using (var slowHost = CreateSignerHost(delayedKeys, shortBudget))
        {
            var slowDone = await SnapshotAsync(slowHost, actor, grant, lease);
            var timer = Stopwatch.StartNew();
            await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable", () => IssueAsync(slowHost, actor, grant, slowDone));
            timer.Stop();
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), $"Delayed resolver waited {timer.Elapsed}");
            await AssertAuthorityLocksFreeAsync(actor);
        }

        // 3. Provider failure during resolution (RequestFailedException) and signing (AuthenticationFailedException).
        var resolveFails = new FaultySigningKeys(_ => throw new RequestFailedException(503, "vault unreachable"));
        using (var failResolveHost = CreateSignerHost(resolveFails, shortBudget))
        {
            var failDone = await SnapshotAsync(failResolveHost, actor, grant, lease);
            await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable", () => IssueAsync(failResolveHost, actor, grant, failDone));
            await AssertAuthorityLocksFreeAsync(actor);
        }

        var signFails = new FaultySigningKeys(_ => ValueTask.FromResult<MerchantBillingSigningKey?>(
            new ThrowingSigningKey(_keyA.Id, () => throw new AuthenticationFailedException("credential unavailable"))));
        using (var failSignHost = CreateSignerHost(signFails, shortBudget))
        {
            var failSignDone = await SnapshotAsync(failSignHost, actor, grant, lease);
            await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable", () => IssueAsync(failSignHost, actor, grant, failSignDone));
            await AssertAuthorityLocksFreeAsync(actor);
        }

        // 4. Unusable local key (public-only PEM passed as LocalPrivateKeyPem -> CryptographicException on SignData).
        using (var publicOnlyHost = CreateHost(configure: services =>
        {
            services.RemoveAll<IOptionsMonitor<MerchantBillingDelegationOptions>>();
            services.AddSingleton<IOptionsMonitor<MerchantBillingDelegationOptions>>(
                new RawLocalPemOptions(_keyB.Id, _keyB.Rsa.ExportSubjectPublicKeyInfoPem()));
        }))
        {
            var pubDone = await SnapshotAsync(publicOnlyHost, actor, grant, lease);
            await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable", () => IssueAsync(publicOnlyHost, actor, grant, pubDone));
            await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable",
                async () => (await ReserveAsync(publicOnlyHost, actor, "p0501-pubonly")).Grant);
            await AssertAuthorityLocksFreeAsync(actor);
        }

        // 5. Caller cancellation is preserved rather than mapped to SigningUnavailable.
        using var callerHost = CreateSignerHost(delayedKeys, TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var signer = callerHost.Services.GetRequiredService<IOfflinePermitSigner>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => signer.SignAsync(OfflinePermitContractV1.PermitType, [1, 2, 3], cts.Token));
        Assert.Equal(new Evidence(1, 0, 1), await EvidenceAsync(actor.Shop));
    }

    private ApiWebApplicationFactory CreateSignerHost(IMerchantBillingSigningKeys testKeys, TimeSpan budget) =>
        CreateHost(configure: services =>
        {
            services.RemoveAll<IOfflinePermitSigner>();
            services.AddSingleton<IOfflinePermitSigner>(provider => new OfflinePermitSigningKeys(
                testKeys,
                provider.GetRequiredService<IOptionsMonitor<MerchantBillingDelegationOptions>>(),
                provider.GetRequiredService<IOptionsMonitor<OfflinePermitKeyOptions>>(),
                budget));
        });

    /// <summary>Proves the licence fence and actor row lock are immediately acquirable on a separate connection.</summary>
    private async Task AssertAuthorityLocksFreeAsync(Actor actor)
    {
        await using var conn = new NpgsqlConnection(Migrator);
        await conn.OpenAsync(Ct);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        await using (var cmd = new NpgsqlCommand("SET LOCAL lock_timeout = '200ms'", conn, tx))
            await cmd.ExecuteNonQueryAsync(Ct);
        await using (var fenceCmd = new NpgsqlCommand(
            $"SELECT public.take_merchant_licence_fence('{actor.Binding.Account:D}'::uuid, '{actor.Shop:D}'::uuid, false)", conn, tx))
            await fenceCmd.ExecuteNonQueryAsync(Ct);
        await using (var userCmd = new NpgsqlCommand($"SELECT 1 FROM users WHERE id = '{actor.User:D}' FOR UPDATE NOWAIT", conn, tx))
            Assert.Equal(1, (int)(await userCmd.ExecuteScalarAsync(Ct))!);
        await tx.RollbackAsync(Ct);
    }

    private sealed class FaultySigningKeys(Func<CancellationToken, ValueTask<MerchantBillingSigningKey?>> resolve)
        : IMerchantBillingSigningKeys
    {
        public ValueTask<MerchantBillingSigningKey?> ResolveAsync(CancellationToken cancellationToken) => resolve(cancellationToken);
    }

    private sealed class ThrowingSigningKey(string id, Func<byte[]> sign) : MerchantBillingSigningKey(id)
    {
        internal override ValueTask<byte[]> SignAsync(byte[] bytes, CancellationToken cancellationToken) => ValueTask.FromResult(sign());
        public override void Dispose() { }
    }

    private sealed class RawLocalPemOptions(string keyId, string pem) : IOptionsMonitor<MerchantBillingDelegationOptions>
    {
        private readonly MerchantBillingDelegationOptions _permit = new() { LocalKeyId = keyId, LocalPrivateKeyPem = pem };
        public MerchantBillingDelegationOptions CurrentValue => Get(null);
        public MerchantBillingDelegationOptions Get(string? name) =>
            name == OfflinePermitContractV1.Section ? _permit : new();
        public IDisposable? OnChange(Action<MerchantBillingDelegationOptions, string?> listener) => null;
    }
}
