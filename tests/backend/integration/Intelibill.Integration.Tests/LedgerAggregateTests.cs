using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class LedgerAggregateTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task LedgerBalances_AllEntryTypesAgree_AndAggregateInSql()
    {
        await using var owner = Owner();
        var user = User.CreateWithEmail($"ledger-{Guid.NewGuid():N}@test.com", "hash", "Ledger", "Reader");
        var shop = Shop.Create("Ledger", "Road", "City", "State", "560001", null, null, null);
        var other = Shop.Create("Foreign", "Road", "City", "State", "560001", null, null, null);
        var customer = Customer.Create(shop.Id, "Positive", "123", null);
        var negative = Customer.Create(shop.Id, "Negative", "124", null);
        var supplier = Supplier.Create(shop.Id, "Positive", null, null, null, null, null, null, true, false);
        var supplierCredit = Supplier.Create(shop.Id, "Credit", null, null, null, null, null, null, true, false);
        var foreignCustomer = Customer.Create(other.Id, "Foreign", "999", null);
        var foreignSupplier = Supplier.Create(other.Id, "Foreign", null, null, null, null, null, null, true, false);
        owner.AddRange(foreignCustomer, foreignSupplier,
            CustomerLedgerEntry.Create(other.Id, foreignCustomer.Id, null, CustomerLedgerEntryType.ReturnCreditReversal, 1000, new(2026, 6, 1), null, user.Id).Value,
            SupplierLedgerEntry.Create(other.Id, foreignSupplier.Id, null, SupplierLedgerEntryType.RecordAdjusted, 1000, new(2026, 6, 1), null, user.Id).Value);
        var sale = Sale.Create(shop.Id, "L-001", customer.Id, customer.Name, null, PaymentMethod.Credit, DateTimeOffset.UtcNow, 100, 0, 0, 100, []);
        owner.AddRange(user, shop, other, customer, negative, supplier, supplierCredit, sale, ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
        var amounts = new[] { 100m, 40m, 10m, 5m, 20m, 15m };
        var types = Enum.GetValues<CustomerLedgerEntryType>();
        Assert.Equal(6, types.Length);
        for (var i = 0; i < types.Length; i++) owner.Add(CustomerLedgerEntry.Create(shop.Id, customer.Id,
            types[i] is CustomerLedgerEntryType.SaleDue or CustomerLedgerEntryType.SaleCancellationCredit ? sale.Id : null, types[i], amounts[i], new(2026, 6, 1), null, user.Id).Value);
        owner.Add(CustomerLedgerEntry.Create(shop.Id, negative.Id, null, CustomerLedgerEntryType.PaymentReceived, 200, new(2026, 6, 1), null, user.Id).Value);
        var item = Item.Create(shop.Id, "Goods", null, "PCS", $"L-{Guid.NewGuid():N}", true, user.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "LOT", 1, 1, 1, 1, 0, false, null, null, supplier.Id, user.Id).Value;
        owner.AddRange(item, batch);
        var supplierAmounts = new[] { 100m, 40m, -10m, -5m, 20m, -15m };
        var supplierTypes = Enum.GetValues<SupplierLedgerEntryType>();
        Assert.Equal(6, supplierTypes.Length);
        for (var i = 0; i < supplierTypes.Length; i++)
        {
            var entry = SupplierLedgerEntry.Create(shop.Id, supplier.Id,
                supplierTypes[i] is SupplierLedgerEntryType.GoodsReceived or SupplierLedgerEntryType.CostAdjusted or SupplierLedgerEntryType.SupplierReturnCredit ? batch.Id : null,
                supplierTypes[i], supplierAmounts[i], new(2026, 6, 1), null, user.Id).Value;
            owner.Add(entry);
        }
        owner.Add(SupplierLedgerEntry.Create(shop.Id, supplier.Id, batch.Id, SupplierLedgerEntryType.CostAdjusted, -7, new(2026, 6, 1), null, user.Id).Value);
        owner.Add(SupplierLedgerEntry.Create(shop.Id, supplier.Id, null, SupplierLedgerEntryType.Reversal, 3, new(2026, 6, 1), null, user.Id).Value);
        owner.Add(SupplierLedgerEntry.Create(shop.Id, supplierCredit.Id, null, SupplierLedgerEntryType.RecordAdjusted, -200, new(2026, 6, 1), null, user.Id).Value);
        await owner.SaveChangesAsync();
        var commands = new DirectoryCommands();
        await using var db = await Runtime(shop.Id, user.Id, commands);
        var customers = new CustomerLedgerEntryRepository(db);
        var suppliers = new SupplierLedgerEntryRepository(db);
        Assert.Equal(60m, await customers.GetCustomerBalanceAsync(shop.Id, customer.Id));
        Assert.Equal(60m, await customers.GetCustomerCreditDueAsync(shop.Id));
        Assert.Equal(60m, (await customers.GetCustomerBalancesAsync(shop.Id, [customer.Id, negative.Id]))[customer.Id]);
        Assert.Equal(46m, await suppliers.GetSupplierBalanceAsync(shop.Id, supplier.Id));
        Assert.Equal(46m, await suppliers.GetSupplierPayablesAsync(shop.Id));
        var groupedSuppliers = await suppliers.GetSupplierBalancesAsync(shop.Id, [supplier.Id, supplierCredit.Id, foreignSupplier.Id]);
        Assert.Equal(46m, groupedSuppliers[supplier.Id]);
        Assert.Equal(-200m, groupedSuppliers[supplierCredit.Id]);
        Assert.False(groupedSuppliers.ContainsKey(foreignSupplier.Id));
        Assert.Empty(await customers.GetCustomerBalancesAsync(shop.Id, [foreignCustomer.Id]));
        Assert.Equal(0m, await customers.GetCustomerBalanceAsync(shop.Id, Guid.NewGuid()));
        Assert.Equal(0m, await suppliers.GetSupplierBalanceAsync(shop.Id, Guid.NewGuid()));
        Assert.Equal(0m, await suppliers.GetSupplierPayablesAsync(other.Id));
        Assert.Equal(0m, await customers.GetCustomerCreditDueAsync(other.Id));
        Assert.Contains(commands.Sql, sql => sql.Contains("sum(", StringComparison.OrdinalIgnoreCase) && sql.Contains("CASE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands.Sql, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        var customerSummary = await new CustomerRepository(db).GetDirectorySummaryAsync(shop.Id,
            new Intelibill.Domain.ValueObjects.UtcDateWindow(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(60m, customerSummary.OutstandingBalance);
        Assert.Equal(1, customerSummary.OutstandingCount);
        var supplierSummary = await new SupplierRepository(db).GetDirectorySummaryAsync(shop.Id, false);
        Assert.Equal(46m, supplierSummary.PendingPayables);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnknownCustomerType_IsRejectedRatherThanAssignedASign()
    {
        // The extension is the authoritative sign contract, including unknown future persisted enum values.
        Assert.Throws<ArgumentOutOfRangeException>(() => ((CustomerLedgerEntryType)999).ToBalanceDelta(10m));
        await using var owner = Owner();
        var user = User.CreateWithEmail($"unknown-{Guid.NewGuid():N}@test.com", "hash", "Ledger", "Reader");
        var shop = Shop.Create("Unknown", "Road", "City", "State", "560001", null, null, null);
        var customer = Customer.Create(shop.Id, "Unknown", "123", null);
        owner.AddRange(user, shop, customer, ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
        var entry = CustomerLedgerEntry.Create(shop.Id, customer.Id, null, CustomerLedgerEntryType.PaymentReceived, 10m, new(2026, 6, 1), null, user.Id).Value;
        owner.Add(entry);
        await owner.SaveChangesAsync();
        await using var transaction = await owner.Database.BeginTransactionAsync();
        await owner.Database.ExecuteSqlRawAsync("ALTER TABLE customer_ledger_entries DROP CONSTRAINT ck_customer_ledger_entries_sale_by_type");
        await owner.Database.ExecuteSqlAsync($"UPDATE customer_ledger_entries SET entry_type = 'FUTURE_TYPE' WHERE id = {entry.Id}");
        await owner.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {RestrictedRuntimeRole.Name}");
        await owner.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.Id.ToString()}, true), set_config('app.current_user_id', {user.Id.ToString()}, true)");
        owner.ChangeTracker.Clear();
        var repository = new CustomerLedgerEntryRepository(owner);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetCustomerBalanceAsync(shop.Id, customer.Id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetCustomerCreditDueAsync(shop.Id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetCustomerBalancesAsync(shop.Id, [customer.Id]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new CustomerRepository(owner).GetDirectorySummaryAsync(shop.Id,
            new Intelibill.Domain.ValueObjects.UtcDateWindow(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1))));
        await transaction.RollbackAsync();
    }

    private ApplicationDbContext Owner() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);
    private async Task<ApplicationDbContext> Runtime(Guid shop, Guid user, DirectoryCommands? commands = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.RuntimeConnectionString).UseSnakeCaseNamingConvention();
        if (commands is not null) options.AddInterceptors(commands);
        var db = new ApplicationDbContext(options.Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.ToString()}, false), set_config('app.current_user_id', {user.ToString()}, false)");
        commands?.Sql.Clear();
        return db;
    }
}
