using Intelibill.Application.Features.Expenses.Queries.GetExpenses;

namespace Intelibill.Application.Unit.Tests.Features.Expenses.Queries.GetExpenses;

public sealed class GetExpensesQueryValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" ALL ")]
    [InlineData("Active")]
    [InlineData("vOiDeD")]
    public void NamedStatus_IsValid(string? status)
    {
        var query = new GetExpensesQuery(Guid.NewGuid(), Guid.NewGuid(), null, Status: status);
        Assert.True(new GetExpensesQueryValidator().Validate(query).IsValid);
    }

    [Theory]
    [InlineData("1", 1, 20, "Expense.InvalidStatus")]
    [InlineData("unknown", 1, 20, "Expense.InvalidStatus")]
    [InlineData("all", 0, 20, "Expense.InvalidPageNumber")]
    [InlineData("all", int.MinValue, 20, "Expense.InvalidPageNumber")]
    [InlineData("all", int.MaxValue, 100, "Expense.InvalidPageNumber")]
    [InlineData("all", 1, 0, "Expense.InvalidPageSize")]
    [InlineData("all", 1, 101, "Expense.InvalidPageSize")]
    public void InvalidInputs_HaveStableCodes(string status, int page, int size, string code)
    {
        var query = new GetExpensesQuery(Guid.NewGuid(), Guid.NewGuid(), null, page, size, status);
        Assert.Contains(new GetExpensesQueryValidator().Validate(query).Errors, failure => failure.ErrorCode == code);
    }
}
