using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ExpenseTests
{
    [Fact]
    public void Create_WithValidData_ReturnsExpense()
    {
        var expense = Expense.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            150.50m,
            " landlord ",
            "Monthly rent",
            new DateOnly(2026, 4, 15),
            Guid.NewGuid());

        Assert.Equal(150.50m, expense.Amount);
        Assert.Equal("landlord", expense.PaidTo);
        Assert.Equal("Monthly rent", expense.Description);
        Assert.False(expense.IsVoided);
        Assert.Null(expense.OriginalExpenseId);
    }

    [Fact]
    public void Create_WithWhitespaceDescription_SetsNull()
    {
        var expense = Expense.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "Vendor",
            "   ",
            new DateOnly(2026, 4, 15),
            Guid.NewGuid());

        Assert.Null(expense.Description);
    }

    [Fact]
    public void Create_WithNullDescription_SetsNull()
    {
        var expense = Expense.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "Vendor",
            null,
            new DateOnly(2026, 4, 15),
            Guid.NewGuid());

        Assert.Null(expense.Description);
    }

    [Fact]
    public void MarkVoided_SetsIsVoidedTrue()
    {
        var expense = Expense.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "Vendor",
            null,
            new DateOnly(2026, 4, 15),
            Guid.NewGuid());

        expense.MarkVoided();

        Assert.True(expense.IsVoided);
    }

    [Fact]
    public void CreateCorrection_SetsOriginalExpenseId()
    {
        var originalId = Guid.NewGuid();
        var expense = Expense.CreateCorrection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            200m,
            "Vendor",
            null,
            new DateOnly(2026, 4, 20),
            Guid.NewGuid(),
            originalId);

        Assert.Equal(originalId, expense.OriginalExpenseId);
        Assert.False(expense.IsVoided);
    }
}
