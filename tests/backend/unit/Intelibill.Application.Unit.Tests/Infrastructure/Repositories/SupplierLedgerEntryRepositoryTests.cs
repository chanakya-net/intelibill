using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class SupplierLedgerEntryRepositoryTests
{
    [Fact]
    public async Task GetSupplierPayablesAsync_ReturnsSumOfPositiveSupplierBalancesForActiveShop()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var otherShop = Shop.Create("Other", "Address", "City", "State", "560002", null, null, null);

        var supplierA = Supplier.Create(shop.Id, "Supplier A", null, null, null, null, null, null, true, false);
        var supplierB = Supplier.Create(shop.Id, "Supplier B", null, null, null, null, null, null, true, false);
        var supplierC = Supplier.Create(shop.Id, "Supplier C", null, null, null, null, null, null, true, false);
        var supplierD = Supplier.Create(shop.Id, "Supplier D", null, null, null, null, null, null, true, false);
        var otherSupplier = Supplier.Create(otherShop.Id, "Supplier X", null, null, null, null, null, null, true, false);

        var entries = new[]
        {
            SupplierLedgerEntry.Create(shop.Id, supplierA.Id, Guid.NewGuid(), SupplierLedgerEntryType.GoodsReceived, 1000m, DateOnly.FromDateTime(new DateTime(2026, 5, 1)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplierA.Id, null, SupplierLedgerEntryType.PaymentMade, 200m, DateOnly.FromDateTime(new DateTime(2026, 5, 2)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplierB.Id, Guid.NewGuid(), SupplierLedgerEntryType.GoodsReceived, 500m, DateOnly.FromDateTime(new DateTime(2026, 5, 3)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplierB.Id, null, SupplierLedgerEntryType.PaymentMade, 700m, DateOnly.FromDateTime(new DateTime(2026, 5, 4)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplierC.Id, null, SupplierLedgerEntryType.RecordAdjusted, -100m, DateOnly.FromDateTime(new DateTime(2026, 5, 5)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplierD.Id, Guid.NewGuid(), SupplierLedgerEntryType.GoodsReceived, 300m, DateOnly.FromDateTime(new DateTime(2026, 5, 6)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplierD.Id, null, SupplierLedgerEntryType.PaymentMade, 300m, DateOnly.FromDateTime(new DateTime(2026, 5, 7)), null, actorId).Value,
            SupplierLedgerEntry.Create(otherShop.Id, otherSupplier.Id, Guid.NewGuid(), SupplierLedgerEntryType.GoodsReceived, 900m, DateOnly.FromDateTime(new DateTime(2026, 5, 8)), null, actorId).Value,
        };

        await context.AddRangeAsync(shop, otherShop, supplierA, supplierB, supplierC, supplierD, otherSupplier, entries[0], entries[1], entries[2], entries[3], entries[4], entries[5], entries[6], entries[7]);
        await context.SaveChangesAsync();

        var repository = new SupplierLedgerEntryRepository(context);

        var result = await repository.GetSupplierPayablesAsync(shop.Id, CancellationToken.None);

        Assert.Equal(800m, result);
    }

    [Fact]
    public async Task GetSupplierPayablesAsync_ReturnsZeroWhenNoPositiveBalances()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var supplier = Supplier.Create(shop.Id, "Supplier A", null, null, null, null, null, null, true, false);

        var entries = new[]
        {
            SupplierLedgerEntry.Create(shop.Id, supplier.Id, Guid.NewGuid(), SupplierLedgerEntryType.GoodsReceived, 400m, DateOnly.FromDateTime(new DateTime(2026, 5, 1)), null, actorId).Value,
            SupplierLedgerEntry.Create(shop.Id, supplier.Id, null, SupplierLedgerEntryType.PaymentMade, 400m, DateOnly.FromDateTime(new DateTime(2026, 5, 2)), null, actorId).Value,
        };

        await context.AddRangeAsync(shop, supplier, entries[0], entries[1]);
        await context.SaveChangesAsync();

        var repository = new SupplierLedgerEntryRepository(context);

        var result = await repository.GetSupplierPayablesAsync(shop.Id, CancellationToken.None);

        Assert.Equal(0m, result);
    }

    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
