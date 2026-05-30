using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.Queries.GetExpenseDetail;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Expenses.Queries.GetExpenseDetail;

public class GetExpenseDetailQueryHandlerTests
{
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();

    private GetExpenseDetailQueryHandler CreateHandler() => new(_expenseRepository);

    [Fact]
    public async Task Handle_WhenNotFound_ReturnsError()
    {
        var expenseId = Guid.NewGuid();
        _expenseRepository.GetByIdAsync(expenseId, Arg.Any<CancellationToken>()).Returns((Expense?)null);

        var result = await CreateHandler().Handle(new GetExpenseDetailQuery(Guid.NewGuid(), Guid.NewGuid(), expenseId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Expense.NotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenDifferentShop_ReturnsNotFound()
    {
        var shopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();
        var expense = Expense.Create(otherShopId, Guid.NewGuid(), 100m, "Vendor", null, new DateOnly(2026, 4, 1), Guid.NewGuid());
        _expenseRepository.GetByIdAsync(expense.Id, Arg.Any<CancellationToken>()).Returns(expense);

        var result = await CreateHandler().Handle(new GetExpenseDetailQuery(Guid.NewGuid(), shopId, expense.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Expense.NotFound.Code, result.FirstError.Code);
    }
}
