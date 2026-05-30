using Intelibill.Application.Features.Expenses.Queries.GetExpenses;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Expenses.Queries.GetExpenses;

public class GetExpensesQueryHandlerTests
{
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();

    private GetExpensesQueryHandler CreateHandler() => new(_expenseRepository);

    [Fact]
    public async Task Handle_ReturnsPaginatedList()
    {
        var shopId = Guid.NewGuid();
        var category = ExpenseCategory.Create(shopId, "Rent", DateTimeOffset.UtcNow);
        var expense = Expense.Create(shopId, category.Id, 500m, "Landlord", null, new DateOnly(2026, 4, 1), Guid.NewGuid());
        typeof(Expense).GetProperty("Category")!.SetValue(expense, category);

        _expenseRepository.GetPagedByShopAsync(shopId, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([expense], 1));

        var result = await CreateHandler().Handle(new GetExpensesQuery(Guid.NewGuid(), shopId, null, 1, 20), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Items);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(500m, result.Value.Items[0].Amount);
    }
}
