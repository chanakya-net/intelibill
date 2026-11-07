using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.Commands.CorrectExpense;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Expenses.Commands.CorrectExpense;

public class CorrectExpenseCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IExpenseCategoryRepository _categoryRepository = Substitute.For<IExpenseCategoryRepository>();
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();
    private CorrectExpenseCommandHandler CreateHandler() =>
        new(_userRepository, _categoryRepository, _expenseRepository, _postingPeriods, _unitOfWork);

    private static CorrectExpenseCommand CreateCommand(Guid userId, Guid shopId, Guid originalId) =>
        new(userId, shopId, originalId, "Corrected Category", 300m, "Corrected Vendor", null, new DateOnly(2026, 4, 20));

    private static User CreateActor(Guid userId, Guid shopId, ShopRole role)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        typeof(User).GetProperty("Id")!.SetValue(user, userId);
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        typeof(Shop).GetProperty("Id")!.SetValue(shop, shopId);
        user.AddShopMembership(ShopMembership.Create(shop.Id, userId, role, true));
        return user;
    }

    [Theory]
    [InlineData(ExpenseTreatment.Operating)]
    [InlineData(ExpenseTreatment.CapitalOrOtherExcluded)]
    public async Task SupplierCorrection_RefusesTreatmentChangeWithoutVoiding(ExpenseTreatment treatment)
    {
        var userId = Guid.NewGuid(); var shopId = Guid.NewGuid();
        var original = Expense.CreateFromSupplierPayment(shopId, Guid.NewGuid(), 100m, "Supplier", null, new DateOnly(2026, 4, 1), userId, Guid.NewGuid());
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(CreateActor(userId, shopId, ShopRole.Owner));
        _expenseRepository.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId, original.Id) with { Treatment = treatment }, CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Expense.InvalidTreatment", result.FirstError.Code);
        Assert.False(original.IsVoided);
    }

    [Fact]
    public async Task SupplierCorrection_PreservesProvenanceThroughTwoGenerations()
    {
        var userId = Guid.NewGuid(); var shopId = Guid.NewGuid(); var ledgerId = Guid.NewGuid();
        var original = Expense.CreateFromSupplierPayment(shopId, Guid.NewGuid(), 100m, "Supplier", null, new DateOnly(2026, 4, 1), userId, ledgerId);
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(CreateActor(userId, shopId, ShopRole.Owner));
        _categoryRepository.GetByNameAsync(shopId, "Corrected Category", Arg.Any<CancellationToken>()).Returns(ExpenseCategory.Create(shopId, "Corrected Category", DateTimeOffset.UtcNow));
        for (var generation = 0; generation < 2; generation++)
        {
            _expenseRepository.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
            Expense? saved = null;
            _expenseRepository.AddAsync(Arg.Do<Expense>(e => saved = e), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId, original.Id), CancellationToken.None);
            Assert.False(result.IsError);
            Assert.Equal(ledgerId, result.Value.SupplierLedgerEntryId);
            Assert.Equal(ExpenseTreatment.SupplierSettlement, result.Value.Treatment);
            Assert.NotNull(saved);
            original = saved;
        }
    }

    [Fact]
    public async Task HandleAsync_WhenExpenseNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var actor = CreateActor(userId, shopId, ShopRole.Owner);
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(actor);
        _expenseRepository.GetByIdAsync(originalId, Arg.Any<CancellationToken>()).Returns((Expense?)null);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId, originalId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Expense.NotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyVoided_ReturnsAlreadyVoided()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var actor = CreateActor(userId, shopId, ShopRole.Owner);
        var original = Expense.Create(shopId, Guid.NewGuid(), 100m, "Vendor", null, new DateOnly(2026, 4, 1), userId);
        original.MarkVoided();
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(actor);
        _expenseRepository.GetByIdAsync(originalId, Arg.Any<CancellationToken>()).Returns(original);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId, originalId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Expense.AlreadyVoided.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_VoidsOriginalAndCreatesCorrected()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var actor = CreateActor(userId, shopId, ShopRole.Owner);
        var categoryId = Guid.NewGuid();
        var original = Expense.Create(shopId, categoryId, 100m, "Vendor", null, new DateOnly(2026, 4, 1), userId);
        var existingCategory = ExpenseCategory.Create(shopId, "Corrected Category", DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(actor);
        _expenseRepository.GetByIdAsync(originalId, Arg.Any<CancellationToken>()).Returns(original);
        _categoryRepository.GetByNameAsync(shopId, "Corrected Category", Arg.Any<CancellationToken>()).Returns(existingCategory);

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId, originalId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(original.IsVoided);
        Assert.Equal(originalId, result.Value.OriginalExpenseId);
        Assert.Equal(300m, result.Value.Amount);
        _expenseRepository.Received(1).Update(original);
        await _expenseRepository.Received(1).AddAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClosedPeriod_OfTheOriginalDate_RefusesCorrection_AndVoidsNothing()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var original = Expense.Create(shopId, Guid.NewGuid(), 100m, "Vendor", null, new DateOnly(2026, 3, 28), userId);
        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>()).Returns(CreateActor(userId, shopId, ShopRole.Owner));
        _expenseRepository.GetByIdAsync(originalId, Arg.Any<CancellationToken>()).Returns(original);
        _postingPeriods.AuthorizeForWriteAsync(
                shopId,
                Arg.Is<IReadOnlyCollection<DateOnly>>(d => d.Count == 2 && d.Contains(new DateOnly(2026, 3, 28)) && d.Contains(new DateOnly(2026, 4, 20))),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<IReadOnlyList<PostingPeriodLease>>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 3, 1))));

        var result = await CreateHandler().HandleAsync(CreateCommand(userId, shopId, originalId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        _expenseRepository.DidNotReceive().Update(Arg.Any<Expense>());
        await _expenseRepository.DidNotReceive().AddAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }
}
