using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.Queries.GetSuppliers;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Suppliers.Queries.GetSuppliers;

public class GetSuppliersQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly ISupplierLedgerEntryRepository _supplierLedgerEntryRepository = Substitute.For<ISupplierLedgerEntryRepository>();

    [Fact]
    public async Task HandleAsync_WhenCallerNotInActiveShop_ReturnsForbidden()
    {
        var caller = User.CreateWithEmail("member@test.com", "hash", "Member", "One");
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var handler = new GetSuppliersQueryHandler(_userRepository, _shopRepository, _supplierRepository, _supplierLedgerEntryRepository);
        var result = await handler.HandleAsync(new GetSuppliersQuery(caller.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReturnsOwnerSuppliers()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var caller = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var managerMembership = ShopMembership.Create(shop.Id, caller.Id, ShopRole.Manager, false);
        shop.AddMembership(ownerMembership);
        shop.AddMembership(managerMembership);
        caller.AddShopMembership(managerMembership);

        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);

        var suppliers = new[]
        {
            Supplier.Create(owner.Id, "A Supplier", null, null, "Address", "City", "State", "560001", 0m, SupplierStatus.IWillReceive, true, false),
            Supplier.Create(owner.Id, "B Supplier", "Person", "+919999999999", "Address 2", "City", "State", "560002", 1000m, SupplierStatus.INeedToPay, true, true),
        };
        _supplierRepository.GetByOwnerUserIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(suppliers);

        _supplierLedgerEntryRepository.GetSupplierBalanceAsync(shop.Id, suppliers[0].Id, Arg.Any<CancellationToken>()).Returns(0m);
        _supplierLedgerEntryRepository.GetSupplierBalanceAsync(shop.Id, suppliers[1].Id, Arg.Any<CancellationToken>()).Returns(500m);

        var handler = new GetSuppliersQueryHandler(_userRepository, _shopRepository, _supplierRepository, _supplierLedgerEntryRepository);
        var result = await handler.HandleAsync(new GetSuppliersQuery(caller.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("A Supplier", result.Value[0].Name);
        Assert.True(result.Value[1].IsPreferred);
        Assert.Equal(0m, result.Value[0].BalanceDue);
        Assert.Equal(500m, result.Value[1].BalanceDue);
    }
}