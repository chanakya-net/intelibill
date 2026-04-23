using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.Commands.RecordExpense;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Expenses.Commands.RecordExpense;

public class RecordExpenseCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IExpenseCategoryRepository _categoryRepository = Substitute.For<IExpenseCategoryRepository>();
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RecordExpenseCommandHandler CreateHandler() =>
        new(_userRepository, _categoryRepository, _expenseRepository, _unitOfWork);

    private static RecordExpenseCommand CreateCommand(Guid userId, Guid shopId) =>
        new(userId, shopId, "Office Supplies", 250m, "Stationery Store", "Printer paper", new DateOnly(2026, 4, 15));

    private static User CreateActor(Guid userId, Guid shopId, ShopRole role)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        typeof(User).GetProperty("Id")!.SetValue(user, userId);
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        typeof(Shop).GetProperty("Id")!.SetValue(shop, shopId);
        user.AddShopMembership(ShopMembership.Create(shop.Id, userId, role, true));
        return user;
    }

    [Fact]
    public async Task HandleAsync_WhenUserNotFound_ReturnsAuthError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenStaff_ReturnsForbidden()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var actor = CreateActor(userId, shopId, ShopRole.Staff);
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Expense.Forbidden.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenNewCategory_CreatesCategoryAndExpense()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var actor = CreateActor(userId, shopId, ShopRole.Owner);
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(actor);
        _categoryRepository.GetByNameAsync(shopId, "Office Supplies", Arg.Any<CancellationToken>()).Returns((ExpenseCategory?)null);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Office Supplies", result.Value.CategoryName);
        await _categoryRepository.Received(1).AddAsync(Arg.Any<ExpenseCategory>(), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExistingCategory_ReusesCategory()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var actor = CreateActor(userId, shopId, ShopRole.Manager);
        var existingCategory = ExpenseCategory.Create(shopId, "Office Supplies", DateTimeOffset.UtcNow);
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(actor);
        _categoryRepository.GetByNameAsync(shopId, "Office Supplies", Arg.Any<CancellationToken>()).Returns(existingCategory);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(existingCategory.Id, result.Value.CategoryId);
        await _categoryRepository.Received(0).AddAsync(Arg.Any<ExpenseCategory>(), Arg.Any<CancellationToken>());
    }
}
