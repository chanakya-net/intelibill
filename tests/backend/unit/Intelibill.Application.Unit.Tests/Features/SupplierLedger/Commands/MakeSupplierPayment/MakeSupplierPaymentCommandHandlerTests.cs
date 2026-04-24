using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.SupplierLedger.Commands.MakeSupplierPayment;

public class MakeSupplierPaymentCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IExpenseCategoryRepository _expenseCategoryRepository = Substitute.For<IExpenseCategoryRepository>();
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private MakeSupplierPaymentCommandHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _supplierRepository, _ledgerRepository, _expenseCategoryRepository, _expenseRepository, _unitOfWork);

    private static (User owner, Shop shop, Supplier supplier) BuildOwnerShopSupplier()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        owner.AddShopMembership(membership);
        shop.AddMembership(membership);
        var supplier = Supplier.Create(owner.Id, "Fresh Foods", null, null, "42 MG Road", "Bengaluru", "Karnataka", "560001", true, false);
        return (owner, shop, supplier);
    }

    private static MakeSupplierPaymentCommand BuildCommand(Guid actorId, Guid shopId, Guid supplierId, decimal amount = 500m) =>
        new(actorId, shopId, supplierId, amount, DateOnly.FromDateTime(DateTime.UtcNow), null);

    [Fact]
    public async Task HandleAsync_OwnerRole_ReturnsSuccessDto()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var category = ExpenseCategory.Create(shop.Id, "Supplier Payments", DateTimeOffset.UtcNow);
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _expenseCategoryRepository.GetByNameAsync(shop.Id, "Supplier Payments", Arg.Any<CancellationToken>()).Returns(category);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplier.Id, result.Value.SupplierId);
        await _ledgerRepository.Received(1).AddAsync(Arg.Is<SupplierLedgerEntry>(e =>
            e.SupplierId == supplier.Id &&
            e.EntryType == SupplierLedgerEntryType.PaymentMade &&
            e.Amount == 500m), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.ShopId == shop.Id &&
            e.CategoryId == category.Id &&
            e.Amount == 500m &&
            e.PaidTo == "Supplier" &&
            e.SupplierLedgerEntryId.HasValue), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MissingCategory_CreatesCategoryAndExpenseInSameSave()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _expenseCategoryRepository.GetByNameAsync(shop.Id, "Supplier Payments", Arg.Any<CancellationToken>()).Returns((ExpenseCategory?)null);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        await _expenseCategoryRepository.Received(1).AddAsync(Arg.Is<ExpenseCategory>(c =>
            c.ShopId == shop.Id &&
            c.Name == "Supplier Payments"), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.ShopId == shop.Id &&
            e.PaidTo == "Supplier" &&
            e.SupplierLedgerEntryId == result.Value.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ManagerRole_ReturnsSuccess()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        manager.AddShopMembership(ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false));

        _userRepository.GetByIdWithDetailsAsync(manager.Id, Arg.Any<CancellationToken>()).Returns(manager);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(BuildCommand(manager.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_StaffRole_ReturnsForbidden()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        staff.AddShopMembership(ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false));

        _userRepository.GetByIdWithDetailsAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);

        var result = await CreateHandler().HandleAsync(BuildCommand(staff.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ActorNotFound_ReturnsUserNotFound()
    {
        var actorId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().HandleAsync(BuildCommand(actorId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_MembershipNotFound_ReturnsMembershipError()
    {
        var actor = User.CreateWithEmail("actor@test.com", "hash", "Actor", "User");
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(BuildCommand(actor.Id, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_SupplierNotFound_ReturnsSupplierNotFound()
    {
        var (owner, shop, _) = BuildOwnerShopSupplier();
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _supplierRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Supplier?)null);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_SupplierOwnerMismatch_ReturnsSupplierNotFound()
    {
        var (owner, shop, _) = BuildOwnerShopSupplier();
        var otherOwner = User.CreateWithEmail("other@test.com", "hash", "Other", "Owner");
        var foreignSupplier = Supplier.Create(otherOwner.Id, "Other Foods", null, null, "1 Main St", "Chennai", "TN", "600001", true, false);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _supplierRepository.GetByIdAsync(foreignSupplier.Id, Arg.Any<CancellationToken>()).Returns(foreignSupplier);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, foreignSupplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }
}
