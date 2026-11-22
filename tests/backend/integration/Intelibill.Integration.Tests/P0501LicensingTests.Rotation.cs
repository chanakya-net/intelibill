using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    [Fact]
    public async Task Rotation_preserves_only_public_historical_proof()
    {
        var verified = At("2026-11-20T12:00:00Z");
        Actor actor;
        Guid lease, grant;
        OfflinePermitEvidence first;
        using (var hostA = CreateHost(_keyA))
        {
            await hostA.InitializeAsync();
            using var client = CreateClient(hostA);
            actor = await OpenShopAsync(client, verified, verified.AddDays(30));
            _clock.Now = verified.AddHours(1);
            (lease, var captured) = await ReserveAsync(hostA, actor);
            grant = Issued(captured).GrantId;
            first = Issued(await IssueAsync(hostA, actor, grant, await SnapshotAsync(hostA, actor, lease)));
        }
        var scope = ScopeOf(actor, lease);
        var issuedAt = first.NotBefore;

        // A new process: B is the only configured signing key. A's private half exists nowhere it can reach.
        using var hostB = CreateHost(_keyB);
        _clock.Now = verified.AddHours(30);
        var historical = Issued(await VerifyAsync(hostB, actor, first.SignedPermit, scope, issuedAt));
        Assert.Equal((first.PermitId, _keyA.Id), (historical.PermitId, first.KeyId));
        Assert.Equal("OfflinePermit.OutsideValidity", Code(await VerifyAsync(hostB, actor, first.SignedPermit, scope, first.ValidUntil)));

        // The original grant, signed by A, still anchors new permits, which B now signs.
        var second = Issued(await IssueAsync(hostB, actor, grant, await SnapshotAsync(hostB, actor, lease)));
        Assert.Equal(_keyB.Id, second.KeyId);
        Assert.Equal(second.PermitId, Issued(await VerifyAsync(hostB, actor, second.SignedPermit, scope, _clock.Now)).PermitId);
        await AssertOnlyPublicKeysAsync(actor.Shop, _keyA, _keyB);

        // Signed by a key the shop never retained, or by another key under a retained identifier.
        using var stranger = RSA.Create(2048);
        var payload = Base64Url.DecodeFromChars(first.SignedPermit.Split('.')[1]);
        Assert.Equal("OfflinePermit.UnknownKey", Code(await VerifyAsync(hostB, actor, Forge(stranger, "p0501-stranger", payload), scope, issuedAt)));
        Assert.Equal("OfflinePermit.InvalidSignature", Code(await VerifyAsync(hostB, actor, Forge(stranger, _keyA.Id, payload), scope, issuedAt)));

        // A retained identifier can never be rebound to other material.
        using var rebound = CreateHost(new PermitKey(_keyA.Id, stranger));
        await RefusedAsync(actor.Shop, "OfflinePermit.KeyRebound",
            async () => await IssueAsync(rebound, actor, grant, await SnapshotAsync(rebound, actor, lease)));
        // A key that also signs another purpose, and no key at all, sign nothing.
        using var shared = CreateHost(configure: services => services.AddSingleton<IOptionsMonitor<MerchantBillingDelegationOptions>>(
            new PermitKeyOptions(_keyB, everyPurpose: true)));
        await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable",
            async () => await IssueAsync(shared, actor, grant, await SnapshotAsync(shared, actor, lease)));
        using var keyless = CreateHost(configure: services => services.AddSingleton<IOptionsMonitor<MerchantBillingDelegationOptions>>(
            new PermitKeyOptions(null)));
        await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable",
            async () => await IssueAsync(keyless, actor, grant, await SnapshotAsync(keyless, actor, lease)));
        await RefusedAsync(actor.Shop, "OfflinePermit.SigningUnavailable", async () => (await ReserveAsync(keyless, actor)).Grant);

        // Both permits remain provable after all of that.
        Assert.Equal(first.PermitId, Issued(await VerifyAsync(keyless, actor, first.SignedPermit, scope, issuedAt)).PermitId);
        Assert.Equal(second.PermitId, Issued(await VerifyAsync(keyless, actor, second.SignedPermit, scope, _clock.Now)).PermitId);
    }

    private async Task AssertOnlyPublicKeysAsync(Guid shop, params PermitKey[] expected)
    {
        await using var db = Open(Migrator);
        var rows = await db.Set<OfflineLicenseVerificationKey>().AsNoTracking().Where(x => x.ShopId == shop)
            .OrderBy(x => x.FirstUsedAt).ToListAsync(Ct);
        Assert.Equal(expected.Select(x => (x.Id, x.Fingerprint)), rows.Select(x => (x.KeyId, x.Fingerprint)));
        foreach (var (row, key) in rows.Zip(expected))
        {
            Assert.Equal(key.PublicKey, row.PublicKey);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(row.PublicKey, out var read);
            Assert.Equal(row.PublicKey.Length, read);
            Assert.ThrowsAny<CryptographicException>(() => rsa.ExportParameters(includePrivateParameters: true));
            using var probe = RSA.Create();
            Assert.ThrowsAny<CryptographicException>(() => probe.ImportPkcs8PrivateKey(row.PublicKey, out _));
            _output.WriteLine("Retained key {0}: {1}-byte SPKI, sha256 {2}, first used {3:O}", row.KeyId, row.PublicKey.Length,
                row.Fingerprint, row.FirstUsedAt);
        }
    }

    private static string Forge(RSA key, string keyId, byte[] payload)
    {
        var input = OfflinePermitSignatures.SigningInput(OfflinePermitContractV1.PermitType, keyId, payload);
        return OfflinePermitSignatures.Attach(input,
            key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
