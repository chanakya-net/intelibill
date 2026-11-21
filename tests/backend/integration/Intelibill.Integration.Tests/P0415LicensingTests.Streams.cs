using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

public sealed partial class P0415LicensingTests
{
    [Theory]
    [InlineData("/api/items/stream", "First item")]
    [InlineData("/api/sales/offline-snapshot/stream", "\"metadata\"")]
    public async Task Open_stream_stops_after_acknowledgement(string route, string firstMarker)
    {
        using var key = RSA.Create(2048);
        var rows = new StreamFixture();
        using var host = CreateHost(key, services =>
        {
            services.RemoveAll<IItemCatalogStreamingService>();
            services.RemoveAll<IOfflineSalesSnapshotStreamingService>();
            services.AddSingleton<IItemCatalogStreamingService>(rows);
            services.AddSingleton<IOfflineSalesSnapshotStreamingService>(rows);
        });
        await host.InitializeAsync();
        using var merchant = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(merchant);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);

        try
        {
            using var response = await merchant.GetAsync(route, HttpCompletionOption.ResponseHeadersRead, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var body = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
            Assert.Contains(firstMarker, await body.ReadLineAsync(Ct));
            await DeliverAsync(delivery, key, grant with
            {
                EventId = Guid.NewGuid(),
                Account = grant.Account! with { Revision = 2, Restriction = "Security" },
            });

            rows.Resume.TrySetResult();
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                Assert.Null(await body.ReadLineAsync(deadline.Token));
            });
        }
        finally
        {
            rows.Resume.TrySetResult();
        }
    }

    [Fact]
    public async Task Projection_acknowledgement_waits_for_the_authorized_handoff()
    {
        using var key = RSA.Create(2048);
        using var host = CreateHost(key);
        await host.InitializeAsync();
        using var merchant = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(merchant);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISuspensionAccessReader>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handoff = reader.DeliverAsync(shop, null, async ct =>
        {
            ready.TrySetResult();
            await finish.Task.WaitAsync(ct);
        }, Ct);

        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var restriction = DeliverAsync(delivery, key, grant with
            {
                EventId = Guid.NewGuid(),
                Account = grant.Account! with { Revision = 2, Restriction = "Security" },
            });
            await WaitForProjectionLockAsync();
            Assert.False(restriction.IsCompleted);
            finish.TrySetResult();
            Assert.Null(await handoff);
            await restriction;
        }
        finally
        {
            finish.TrySetResult();
        }
    }

    private async Task WaitForProjectionLockAsync()
    {
        await using var db = OpenMigrator();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            ) AS "Value"
            """).SingleAsync(deadline.Token))
            await Task.Delay(10, deadline.Token);
    }

    private sealed class StreamFixture : IItemCatalogStreamingService, IOfflineSalesSnapshotStreamingService
    {
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ErrorOr<Success>> ValidateAccessAsync(Guid userId, Guid activeShopId, CancellationToken cancellationToken) =>
            Task.FromResult<ErrorOr<Success>>(Result.Success);

        public async IAsyncEnumerable<ItemCatalogEntryDto> StreamByShopAsync(Guid activeShopId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new(Guid.NewGuid(), "First item", "ITEM-1");
            await Resume.Task.WaitAsync(cancellationToken);
            yield return new(Guid.NewGuid(), "Second item", "ITEM-2");
        }

        public async IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(Guid activeShopId, Guid snapshotId,
            DateTimeOffset startedAt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new OfflineSalesSnapshotMetadataRecord(new(snapshotId, activeShopId, 4, startedAt, "Asia/Kolkata", 1));
            await Resume.Task.WaitAsync(cancellationToken);
            yield return new OfflineSalesSnapshotCompleteRecord(new(snapshotId, startedAt, 0, 0, 0, 0));
        }
    }
}
