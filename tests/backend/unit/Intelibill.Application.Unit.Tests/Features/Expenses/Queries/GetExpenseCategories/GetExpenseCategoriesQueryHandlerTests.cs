using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Application.Features.Expenses.Queries.GetExpenseCategories;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Expenses.Queries.GetExpenseCategories;

public class GetExpenseCategoriesQueryHandlerTests
{
    private readonly IExpenseCategoryRepository _categoryRepository = Substitute.For<IExpenseCategoryRepository>();

    [Fact]
    public async Task Handle_ReturnsCategoriesOrderedByName()
    {
        var shopId = Guid.NewGuid();
        var cat1 = ExpenseCategory.Create(shopId, "Rent", DateTimeOffset.UtcNow);
        var cat2 = ExpenseCategory.Create(shopId, "Utilities", DateTimeOffset.UtcNow);

        _categoryRepository.GetByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(new[] { cat1, cat2 });

        var handler = new GetExpenseCategoriesQueryHandler(_categoryRepository);
        var result = await handler.Handle(new GetExpenseCategoriesQuery(Guid.NewGuid(), shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Rent", result.Value[0].Name);
        Assert.Equal("Utilities", result.Value[1].Name);
    }

    [Fact]
    public async Task Handle_WhenNoCategories_ReturnsEmpty()
    {
        var shopId = Guid.NewGuid();
        _categoryRepository.GetByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ExpenseCategory>());

        var handler = new GetExpenseCategoriesQueryHandler(_categoryRepository);
        var result = await handler.Handle(new GetExpenseCategoriesQuery(Guid.NewGuid(), shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }
}
