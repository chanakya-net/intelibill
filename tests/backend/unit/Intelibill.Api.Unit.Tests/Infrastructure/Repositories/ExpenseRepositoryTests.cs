using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Repositories;

public sealed class ExpenseRepositoryTests
{
    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task GetSumByShopAndDateRangeAsync_ExcludesVoidedExpenses()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var date = new DateOnly(2026, 1, 15);

        var active = Expense.Create(shopId, categoryId, 200m, "Vendor", null, date, actorId);
        var voided = Expense.Create(shopId, categoryId, 100m, "Vendor", null, date, actorId);
        voided.MarkVoided();

        await context.Expenses.AddRangeAsync(active, voided);
        await context.SaveChangesAsync();

        var repository = new ExpenseRepository(context);
        var sum = await repository.GetSumByShopAndDateRangeAsync(
            shopId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));

        Assert.Equal(200m, sum);
    }

    [Fact]
    public async Task GetSumByShopAndDateRangeAsync_IncludesOnlyExpensesInRange()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var inRange = Expense.Create(shopId, categoryId, 300m, "Vendor", null, new DateOnly(2026, 1, 15), actorId);
        var outOfRange = Expense.Create(shopId, categoryId, 500m, "Vendor", null, new DateOnly(2026, 2, 1), actorId);

        await context.Expenses.AddRangeAsync(inRange, outOfRange);
        await context.SaveChangesAsync();

        var repository = new ExpenseRepository(context);
        var sum = await repository.GetSumByShopAndDateRangeAsync(
            shopId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));

        Assert.Equal(300m, sum);
    }
}
