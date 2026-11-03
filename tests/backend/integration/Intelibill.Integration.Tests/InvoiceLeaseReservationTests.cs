using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The fiscal series counter against a real database: online numbers and offline ranges share one
/// locked row, so they never overlap, and a series belongs to one shop per GST registration.
/// </summary>
[Collection("Integration Tests")]
public sealed class InvoiceLeaseReservationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly FiscalYear Year = new(2026);
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

    private async Task<Shop> CreateShopAsync(string gstin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = Shop.Create("Lease Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, gstin);
        Assert.False(shop.DeclareTaxProfile(null, "29", TaxRegime.Regular, gstin, null, EInvoiceStatus.BelowThreshold, Guid.NewGuid(), DateTimeOffset.UtcNow).IsError);
        db.Shops.Add(shop);
        await db.SaveChangesAsync();
        return shop;
    }

    /// <summary>Runs one allocation in its own scope and transaction, committed, as a posting or reservation would.</summary>
    private Task<ErrorOr<T>> InTransactionAsync<T>(Func<IInvoiceNumberAllocator, Task<ErrorOr<T>>> allocate) =>
        Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await allocate(scope.ServiceProvider.GetRequiredService<IInvoiceNumberAllocator>());
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return result;
        });

    private static InvoiceSeriesKey Series(Shop shop, string seriesCode = "A", FiscalYear? year = null) =>
        new(shop.Id, shop.SellerRegistrationKey, seriesCode, year ?? Year);

    [Fact]
    public async Task online_and_offline_ranges_do_not_overlap()
    {
        var shop = await CreateShopAsync(TestTaxProfile.NewGstin());
        var series = Series(shop);

        var online = Enumerable.Range(0, 12)
            .Select(_ => InTransactionAsync(allocator => allocator.AllocateAsync(series, CancellationToken.None)))
            .ToList();
        var offline = Enumerable.Range(0, 4)
            .Select(_ => InTransactionAsync(allocator => allocator.ReserveRangeAsync(series, 25, CancellationToken.None)))
            .ToList();
        await Task.WhenAll(online.Cast<Task>().Concat(offline));

        var onlineNumbers = online.Select(task => task.Result.Value).ToList();
        var offlineNumbers = offline
            .SelectMany(task => Enumerable.Range(task.Result.Value.Start, task.Result.Value.End - task.Result.Value.Start + 1)
                .Select(n => InvoiceNumberFormat.Format(task.Result.Value.Prefix, n)))
            .ToList();
        var all = onlineNumbers.Concat(offlineNumbers).ToList();

        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, number => Assert.True(number.Length <= InvoiceNumberFormat.MaxLength, number));
        Assert.All(all, number => Assert.StartsWith("A/2627/", number));
        // Nothing was skipped: the numbers handed out are exactly 1..112.
        Assert.Equal(
            Enumerable.Range(1, 12 + (4 * 25)).Select(n => InvoiceNumberFormat.Format("A/2627/", n)),
            all.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task rolled_back_allocation_leaves_no_gap()
    {
        var shop = await CreateShopAsync(TestTaxProfile.NewGstin());
        var series = Series(shop);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var abandoned = await scope.ServiceProvider.GetRequiredService<IInvoiceNumberAllocator>().AllocateAsync(series, CancellationToken.None);
            await db.SaveChangesAsync();
            Assert.Equal("A/2627/000001", abandoned.Value);
            await transaction.RollbackAsync();
        }

        var next = await InTransactionAsync(allocator => allocator.AllocateAsync(series, CancellationToken.None));

        Assert.Equal("A/2627/000001", next.Value);
    }

    [Fact]
    public async Task fiscal_rollover_uses_shop_business_date()
    {
        var shop = await CreateShopAsync(TestTaxProfile.NewGstin());

        // 31 March 23:59 IST is still 2026-27; 1 April 00:00 IST (31 March 18:30 UTC) starts 2027-28.
        var lastOfYear = FiscalYear.ForDate(new DateTimeOffset(2027, 3, 31, 18, 29, 0, TimeSpan.Zero));
        var firstOfNext = FiscalYear.ForDate(new DateTimeOffset(2027, 3, 31, 18, 30, 0, TimeSpan.Zero));

        var before = await InTransactionAsync(allocator => allocator.AllocateAsync(Series(shop, year: lastOfYear), CancellationToken.None));
        var after = await InTransactionAsync(allocator => allocator.AllocateAsync(Series(shop, year: firstOfNext), CancellationToken.None));

        Assert.Equal("A/2627/000001", before.Value);
        Assert.Equal("A/2728/000001", after.Value);
    }

    [Fact]
    public async Task series_belongs_to_one_shop_per_registration()
    {
        var gstin = TestTaxProfile.NewGstin();
        var first = await CreateShopAsync(gstin);
        var branch = await CreateShopAsync(gstin);

        var owned = await InTransactionAsync(allocator => allocator.AllocateAsync(Series(first), CancellationToken.None));
        var refused = await InTransactionAsync(allocator => allocator.AllocateAsync(Series(branch), CancellationToken.None));
        var ownSeries = await InTransactionAsync(allocator => allocator.AllocateAsync(Series(branch, "B"), CancellationToken.None));

        Assert.Equal("A/2627/000001", owned.Value);
        Assert.Equal("Invoice.SeriesRegisteredToAnotherShop", refused.FirstError.Code);
        Assert.Equal("B/2627/000001", ownSeries.Value);
    }

    [Fact]
    public async Task series_opened_under_another_registration_is_refused()
    {
        var shop = await CreateShopAsync(TestTaxProfile.NewGstin());
        await InTransactionAsync(allocator => allocator.AllocateAsync(Series(shop), CancellationToken.None));

        var changed = await InTransactionAsync(allocator =>
            allocator.AllocateAsync(Series(shop) with { SellerRegistrationKey = TestTaxProfile.NewGstin() }, CancellationToken.None));

        Assert.Equal("Invoice.SeriesRegistrationChanged", changed.FirstError.Code);
    }
}
