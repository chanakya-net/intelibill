using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Repositories;

public sealed class CustomerLedgerEntryRepositoryTests
{
    [Fact]
    public async Task GetCustomerCreditDueAsync_SumsOnlyPositiveCustomerBalancesForShop()
    {
        await using var context = await CreateContextAsync();
        var (shop, customerA, saleA, actorId) = await SeedCustomerAsync(context, invoiceNumber: "INV-001", phoneNumber: "+919000000001");
        var customerB = Customer.Create(shop.Id, "Customer B", "+919000000002", null, true);
        var saleB = Sale.Create(
            shop.Id,
            "INV-002",
            customerB.Id,
            customerB.Name,
            customerB.PhoneNumber,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            0m,
            0m,
            0m,
            0m,
            []);
        var otherShop = Shop.Create("Other", "Address", "City", "State", "560001", null, null, null);
        var otherCustomer = Customer.Create(otherShop.Id, "Customer C", "+919000000003", null, true);
        var otherSale = Sale.Create(
            otherShop.Id,
            "INV-003",
            otherCustomer.Id,
            otherCustomer.Name,
            otherCustomer.PhoneNumber,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            0m,
            0m,
            0m,
            0m,
            []);

        await context.Shops.AddAsync(otherShop);
        await context.Customers.AddRangeAsync(customerB, otherCustomer);
        await context.Sales.AddRangeAsync(saleB, otherSale);
        await context.CustomerLedgerEntries.AddRangeAsync(
            CreateEntry(shop.Id, customerA.Id, saleA.Id, CustomerLedgerEntryType.SaleDue, 100m),
            CreateEntry(shop.Id, customerA.Id, null, CustomerLedgerEntryType.PaymentReceived, 30m),
            CreateEntry(shop.Id, customerB.Id, saleB.Id, CustomerLedgerEntryType.SaleDue, 50m),
            CreateEntry(shop.Id, customerB.Id, null, CustomerLedgerEntryType.PaymentReceived, 70m),
            CreateEntry(otherShop.Id, otherCustomer.Id, otherSale.Id, CustomerLedgerEntryType.SaleDue, 999m));
        await context.SaveChangesAsync();

        var repository = new CustomerLedgerEntryRepository(context);

        var creditDue = await repository.GetCustomerCreditDueAsync(shop.Id);

        Assert.Equal(70m, creditDue);

        CustomerLedgerEntry CreateEntry(
            Guid shopId,
            Guid customerId,
            Guid? saleId,
            CustomerLedgerEntryType entryType,
            decimal amount) =>
            CustomerLedgerEntry.Create(
                shopId,
                customerId,
                saleId,
                entryType,
                amount,
                new DateOnly(2026, 5, 5),
                null,
                actorId).Value;
    }

    [Fact]
    public async Task GetCustomerBalanceAsync_IncludesReturnCreditsAndReversals()
    {
        await using var context = await CreateContextAsync();
        var (shop, customer, sale, actorId) = await SeedCustomerAsync(context);

        await context.CustomerLedgerEntries.AddRangeAsync(
            CreateEntry(shop.Id, customer.Id, sale.Id, CustomerLedgerEntryType.SaleDue, 100m),
            CreateEntry(shop.Id, customer.Id, null, CustomerLedgerEntryType.PaymentReceived, 20m),
            CreateEntry(shop.Id, customer.Id, sale.Id, CustomerLedgerEntryType.ReturnCredit, 30m),
            CreateEntry(shop.Id, customer.Id, null, CustomerLedgerEntryType.ReturnCreditReversal, 10m));
        await context.SaveChangesAsync();

        var repository = new CustomerLedgerEntryRepository(context);

        var balance = await repository.GetCustomerBalanceAsync(shop.Id, customer.Id);

        Assert.Equal(60m, balance);

        CustomerLedgerEntry CreateEntry(
            Guid shopId,
            Guid customerId,
            Guid? saleId,
            CustomerLedgerEntryType entryType,
            decimal amount) =>
            CustomerLedgerEntry.Create(
                shopId,
                customerId,
                saleId,
                entryType,
                amount,
                new DateOnly(2026, 5, 5),
                null,
                actorId).Value;
    }

    [Fact]
    public async Task GetCustomerBalancesAsync_GroupsBalancesByCustomer()
    {
        await using var context = await CreateContextAsync();
        var first = await SeedCustomerAsync(context);
        var second = await SeedCustomerAsync(
            context,
            first.shop,
            invoiceNumber: "INV-002",
            phoneNumber: "+919000000002");

        await context.CustomerLedgerEntries.AddRangeAsync(
            CustomerLedgerEntry.Create(
                first.shop.Id,
                first.customer.Id,
                first.sale.Id,
                CustomerLedgerEntryType.SaleDue,
                100m,
                new DateOnly(2026, 5, 5),
                null,
                first.actorId).Value,
            CustomerLedgerEntry.Create(
                first.shop.Id,
                first.customer.Id,
                null,
                CustomerLedgerEntryType.ReturnCredit,
                25m,
                new DateOnly(2026, 5, 5),
                null,
                first.actorId).Value,
            CustomerLedgerEntry.Create(
                second.shop.Id,
                second.customer.Id,
                second.sale.Id,
                CustomerLedgerEntryType.ReturnCreditReversal,
                15m,
                new DateOnly(2026, 5, 5),
                null,
                second.actorId).Value);
        await context.SaveChangesAsync();

        var repository = new CustomerLedgerEntryRepository(context);

        var balances = await repository.GetCustomerBalancesAsync(
            first.shop.Id,
            [first.customer.Id, second.customer.Id]);

        Assert.Equal(75m, balances[first.customer.Id]);
        Assert.Equal(15m, balances[second.customer.Id]);
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

    private static async Task<(Shop shop, Customer customer, Sale sale, Guid actorId)> SeedCustomerAsync(
        ApplicationDbContext context,
        Shop? shop = null,
        string invoiceNumber = "INV-001",
        string phoneNumber = "+919000000001")
    {
        var actorId = Guid.NewGuid();
        shop ??= Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var customer = Customer.Create(shop.Id, $"Customer {invoiceNumber}", phoneNumber, null, true);
        var sale = Sale.Create(
            shop.Id,
            invoiceNumber,
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            0m,
            100m,
            100m,
            0m,
            []);

        if (context.Entry(shop).State == EntityState.Detached)
        {
            await context.Shops.AddAsync(shop);
        }
        await context.Customers.AddAsync(customer);
        await context.Sales.AddAsync(sale);
        await context.SaveChangesAsync();

        return (shop, customer, sale, actorId);
    }
}
