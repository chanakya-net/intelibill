using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class InvoiceLeaseReservationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    [Fact]
    public async Task ReserveAsync_WhenConcurrent_DoesNotOverlapRanges()
    {
        using var setupScope = _factory.Services.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shop = Shop.Create("Lease Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        var reservedAt = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero);
        var fiscalYear = FiscalYear.ForDate(reservedAt);
        var expiresAt = reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays);

        Task<InvoiceLease> ReserveAsync(string deviceId)
        {
            return Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IInvoiceLeaseRepository>();
                return await repository.ReserveAsync(
                    shop.Id,
                    deviceId,
                    fiscalYear.StartYear,
                    fiscalYear.InvoicePrefix,
                    200,
                    reservedAt,
                    expiresAt);
            });
        }

        var tasks = new[] { ReserveAsync("device-1"), ReserveAsync("device-2") };

        var leases = await Task.WhenAll(tasks);

        Assert.Equal(2, leases.Length);
        var ordered = leases
            .Select(lease => (lease.RangeStart, lease.RangeEnd))
            .OrderBy(range => range.RangeStart)
            .ToList();

        Assert.True(ordered[0].RangeEnd < ordered[1].RangeStart);
    }
}
