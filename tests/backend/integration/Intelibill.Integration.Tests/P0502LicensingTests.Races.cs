using System.Runtime.CompilerServices;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0502LicensingTests
{
    [Fact]
    public async Task Concurrent_bound_snapshots_stay_bound_to_their_own_lease()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var deadline = verified.AddHours(48);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var clientA = CreateClient(host);
        using var clientB = CreateClient(host);
        var shopA = await OpenShopAsync(clientA, verified, verified.AddDays(30));
        var shopB = await OpenShopAsync(clientB, verified, verified.AddHours(12));

        _clock.Now = verified.AddHours(1);
        var leaseA = await ReserveHttpAsync(clientA, "device-a");
        var leaseB = await ReserveHttpAsync(clientB, "device-b");
        var leaseIdA = leaseA.GetProperty("leaseId").GetGuid();
        var evidenceA = RequireLicenceEvidence(leaseA);
        var grantIdA = evidenceA.GetProperty("grantId").GetGuid();
        var grantShaA = evidenceA.GetProperty("grantSha256").GetString()!;
        var leaseIdB = leaseB.GetProperty("leaseId").GetGuid();
        var evidenceB = RequireLicenceEvidence(leaseB);
        var grantIdB = evidenceB.GetProperty("grantId").GetGuid();
        var grantShaB = evidenceB.GetProperty("grantSha256").GetString()!;

        // Four concurrent bound streams on shop A plus two concurrent streams on shop B in parallel.
        var tasksA = Enumerable.Range(0, 4)
            .Select(_ => StreamSnapshotHttpAsync(clientA, BoundStreamUrl(leaseIdA, grantIdA, "device-a")))
            .ToArray();
        var tasksB = Enumerable.Range(0, 2)
            .Select(_ => StreamSnapshotHttpAsync(clientB, BoundStreamUrl(leaseIdB, grantIdB, "device-b")))
            .ToArray();

        var resultsA = await Task.WhenAll(tasksA);
        var resultsB = await Task.WhenAll(tasksB);

        var snapshotsA = new HashSet<Guid>();
        foreach (var stream in resultsA)
        {
            var record = stream.RequirePermitImmediatelyBeforeComplete();
            Assert.Equal(("issued", leaseIdA, grantIdA, deadline),
                (record.GetProperty("status").GetString(), record.GetProperty("leaseId").GetGuid(),
                 record.GetProperty("grantId").GetGuid(), record.GetProperty("validUntil").GetDateTimeOffset()));
            var proven = Issued(await VerifyAsync(host, shopA, record.GetProperty("signedPermit").GetString(),
                ScopeOf(shopA, leaseIdA, "device-a"), _clock.Now));
            Assert.Equal((shopA.Shop, grantShaA, deadline), (proven.ShopId, proven.GrantSha256, proven.ValidUntil));
            Assert.True(snapshotsA.Add(proven.SnapshotId));
        }

        foreach (var stream in resultsB)
        {
            var record = stream.RequirePermitImmediatelyBeforeComplete();
            Assert.Equal(("issued", leaseIdB, grantIdB, verified.AddHours(12)),
                (record.GetProperty("status").GetString(), record.GetProperty("leaseId").GetGuid(),
                 record.GetProperty("grantId").GetGuid(), record.GetProperty("validUntil").GetDateTimeOffset()));
            var proven = Issued(await VerifyAsync(host, shopB, record.GetProperty("signedPermit").GetString(),
                ScopeOf(shopB, leaseIdB, "device-b"), _clock.Now));
            Assert.Equal((shopB.Shop, grantShaB, verified.AddHours(12)), (proven.ShopId, proven.GrantSha256, proven.ValidUntil));
        }

        Assert.Equal(new Evidence(1, 4, 1), await EvidenceAsync(shopA.Shop));
        Assert.Equal(new Evidence(1, 2, 1), await EvidenceAsync(shopB.Shop));
    }

    [Fact]
    public async Task Restriction_before_completion_withholds_the_permit()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var beforeComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = false;

        using var host = CreateHost(configure: services =>
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(IOfflineSalesSnapshotStreamingService));
            services.Remove(descriptor);
            services.AddScoped<IOfflineSalesSnapshotStreamingService>(provider =>
            {
                var inner = (IOfflineSalesSnapshotStreamingService)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
                return new HookedStreamingService(inner, async () =>
                {
                    if (!armed) return;
                    beforeComplete.TrySetResult();
                    await releaseComplete.Task;
                });
            });
        });
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        _clock.Now = verified.AddHours(1);
        var lease = await ReserveHttpAsync(client);
        var leaseId = lease.GetProperty("leaseId").GetGuid();
        var grantId = RequireLicenceEvidence(lease).GetProperty("grantId").GetGuid();

        armed = true;
        var streamTask = StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device));
        await beforeComplete.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // A trading restriction commits after snapshot streaming started and before permit issuance runs.
        await ApplyAsync(actor.Binding, verified.AddDays(30), revision: 2, restriction: "Trading", suspensionEffectiveAt: _clock.Now);
        releaseComplete.TrySetResult();

        var stream = await streamTask;
        var record = stream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(("refused", "Licence.TradingRestricted"),
            (record.GetProperty("status").GetString(), record.GetProperty("refusalCode").GetString()));
        Assert.Equal(new Evidence(1, 0, 1), await EvidenceAsync(actor.Shop));
        _output.WriteLine("Restriction committed mid-stream withheld permit with {0}; evidence={1}",
            record.GetProperty("refusalCode").GetString(), await EvidenceAsync(actor.Shop));
    }

    private sealed class HookedStreamingService(
        IOfflineSalesSnapshotStreamingService inner, Func<Task> beforeComplete) : IOfflineSalesSnapshotStreamingService
    {
        public Task<ErrorOr<Success>> ValidateAccessAsync(Guid userId, Guid activeShopId, CancellationToken cancellationToken) =>
            inner.ValidateAccessAsync(userId, activeShopId, cancellationToken);

        public async IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(
            Guid activeShopId, Guid snapshotId, DateTimeOffset startedAt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var record in inner.StreamAsync(activeShopId, snapshotId, startedAt, cancellationToken))
            {
                if (record is OfflineSalesSnapshotCompleteRecord)
                    await beforeComplete();
                yield return record;
            }
        }
    }
}
