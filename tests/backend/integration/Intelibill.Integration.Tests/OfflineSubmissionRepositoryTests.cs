using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class OfflineSubmissionRepositoryTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static OfflineSubmission Candidate(Guid shopId, Guid userId, string hash, DateTimeOffset receivedAt) =>
        OfflineSubmission.Record(
            shopId, "device-1", "client-1", "INV-0001", hash, "{\"amount\":10}",
            receivedAt.AddDays(-1), receivedAt, OfflineSubmissionSource.Sync, userId, null, null, null, null);

    private async Task<Shop> CreateShopAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = Shop.Create("Envelope Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        await db.SaveChangesAsync();
        return shop;
    }

    [Fact]
    public async Task RecordAsync_IdenticalResend_ReturnsFirstRowAndChangedPayloadAddsSecond()
    {
        var shop = await CreateShopAsync();
        var user = Guid.NewGuid();
        var first = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero);

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOfflineSubmissionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stored = await repository.RecordAsync(Candidate(shop.Id, user, new string('a', 64), first), CancellationToken.None);
        var resent = await repository.RecordAsync(Candidate(shop.Id, user, new string('a', 64), first.AddDays(3)), CancellationToken.None);
        var changed = await repository.RecordAsync(Candidate(shop.Id, user, new string('b', 64), first.AddDays(3)), CancellationToken.None);

        Assert.Equal(stored.Id, resent.Id);
        Assert.Equal(first, resent.ReceivedAt);
        Assert.NotEqual(stored.Id, changed.Id);
        Assert.Equal(2, await db.OfflineSubmissions.CountAsync(s => s.ShopId == shop.Id));

        var earliest = await repository.FindEarliestByHashAsync(shop.Id, "device-1", "client-1", new string('a', 64), CancellationToken.None);
        Assert.Equal(stored.Id, earliest!.Id);
    }

    [Fact]
    public async Task RecordAsync_InsideOpenTransaction_JoinsIt()
    {
        var shop = await CreateShopAsync();
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOfflineSubmissionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await repository.RecordAsync(
                Candidate(shop.Id, Guid.NewGuid(), new string('c', 64), DateTimeOffset.UtcNow), CancellationToken.None);
            await transaction.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.OfflineSubmissions.CountAsync(s => s.ShopId == shop.Id));
    }

    [Fact]
    public async Task HasDeviceHistoryAsync_AndFindReservedRange_UseLeasesAndSyncEnvelopes()
    {
        var shop = await CreateShopAsync();
        var leaseUser = Guid.NewGuid();
        var syncUser = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var reservedAt = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero);
        var fiscalYear = FiscalYear.ForDate(reservedAt);

        using var scope = _factory.Services.CreateScope();
        var leases = scope.ServiceProvider.GetRequiredService<IInvoiceLeaseRepository>();
        var repository = scope.ServiceProvider.GetRequiredService<IOfflineSubmissionRepository>();

        var lease = await leases.ReserveAsync(
            shop.Id, "device-1", fiscalYear.StartYear, fiscalYear.InvoicePrefix, 50, reservedAt,
            reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays), leaseUser, Guid.NewGuid(), CancellationToken.None);
        await repository.RecordAsync(Candidate(shop.Id, syncUser, new string('d', 64), reservedAt), CancellationToken.None);

        Assert.True(await repository.HasDeviceHistoryAsync(shop.Id, "device-1", leaseUser, CancellationToken.None));
        Assert.True(await repository.HasDeviceHistoryAsync(shop.Id, "device-1", syncUser, CancellationToken.None));
        Assert.False(await repository.HasDeviceHistoryAsync(shop.Id, "device-1", stranger, CancellationToken.None));
        Assert.False(await repository.HasDeviceHistoryAsync(shop.Id, "device-2", leaseUser, CancellationToken.None));

        var number = lease.FormatInvoiceNumber(lease.RangeStart + 3);
        var found = await leases.FindReservedRangeAsync(shop.Id, "device-1", number, CancellationToken.None);
        Assert.Equal(lease.Id, found!.Id);
        Assert.Equal(leaseUser, found.ReservedByUserId);
        Assert.Null(await leases.FindReservedRangeAsync(shop.Id, "device-1", lease.FormatInvoiceNumber(lease.RangeEnd + 5), CancellationToken.None));
    }
}
