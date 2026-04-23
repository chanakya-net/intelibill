using Intelibill.Application.Features.SupplierLedger.Events;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.SupplierLedger.Events;

public class SupplierPaymentRecordedHandlerTests
{
    private readonly IExpenseCategoryRepository _categoryRepository = Substitute.For<IExpenseCategoryRepository>();
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SupplierPaymentRecordedHandler CreateHandler() =>
        new(_categoryRepository, _expenseRepository, _unitOfWork);

    private static SupplierPaymentRecorded CreateEvent(decimal amount = 500m, string? notes = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            DateOnly.FromDateTime(DateTime.UtcNow),
            Guid.NewGuid(),
            Guid.NewGuid(),
            notes);

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_CreatesCategoryAndExpense()
    {
        var evt = CreateEvent();
        _categoryRepository.GetByNameAsync(evt.ShopId, "Supplier Payments", Arg.Any<CancellationToken>()).Returns((ExpenseCategory?)null);

        await CreateHandler().HandleAsync(evt, CancellationToken.None);

        await _categoryRepository.Received(1).AddAsync(Arg.Is<ExpenseCategory>(c =>
            c.ShopId == evt.ShopId && c.Name == "Supplier Payments"), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.ShopId == evt.ShopId &&
            e.Amount == evt.Amount &&
            e.ExpenseDate == evt.EntryDate &&
            e.ActorUserId == evt.CreatedBy &&
            e.SupplierLedgerEntryId == evt.SupplierLedgerEntryId &&
            e.PaidTo == "Supplier"), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryExists_ReusesCategory()
    {
        var evt = CreateEvent();
        var existingCategory = ExpenseCategory.Create(evt.ShopId, "Supplier Payments", DateTimeOffset.UtcNow);
        _categoryRepository.GetByNameAsync(evt.ShopId, "Supplier Payments", Arg.Any<CancellationToken>()).Returns(existingCategory);

        await CreateHandler().HandleAsync(evt, CancellationToken.None);

        await _categoryRepository.Received(0).AddAsync(Arg.Any<ExpenseCategory>(), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.CategoryId == existingCategory.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNotes_PassesNotesToDescription()
    {
        var evt = CreateEvent(notes: "Advance payment");
        var existingCategory = ExpenseCategory.Create(evt.ShopId, "Supplier Payments", DateTimeOffset.UtcNow);
        _categoryRepository.GetByNameAsync(evt.ShopId, "Supplier Payments", Arg.Any<CancellationToken>()).Returns(existingCategory);

        await CreateHandler().HandleAsync(evt, CancellationToken.None);

        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.Description == "Advance payment"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNullNotes_PassesNullDescription()
    {
        var evt = CreateEvent(notes: null);
        var existingCategory = ExpenseCategory.Create(evt.ShopId, "Supplier Payments", DateTimeOffset.UtcNow);
        _categoryRepository.GetByNameAsync(evt.ShopId, "Supplier Payments", Arg.Any<CancellationToken>()).Returns(existingCategory);

        await CreateHandler().HandleAsync(evt, CancellationToken.None);

        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.Description == null), Arg.Any<CancellationToken>());
    }
}
