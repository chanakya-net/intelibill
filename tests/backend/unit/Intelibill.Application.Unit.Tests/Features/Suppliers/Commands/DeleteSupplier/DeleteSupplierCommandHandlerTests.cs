using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.Commands.DeleteSupplier;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Suppliers.Commands.DeleteSupplier;

public class DeleteSupplierCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenSystemSupplier_ReturnsCannotModifySystemSupplier()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(systemSupplier.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);

        var handler = new DeleteSupplierCommandHandler(_userRepository, _supplierRepository, _unitOfWork);
        var result = await handler.HandleAsync(new DeleteSupplierCommand(actor.Id, shop.Id, systemSupplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.CannotModifySystemSupplier.Code, result.FirstError.Code);
        _supplierRepository.DidNotReceive().Remove(Arg.Any<Supplier>());
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierBelongsToDifferentShop_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var otherShopId = Guid.NewGuid();
        var supplier = Supplier.Create(otherShopId, "Other Shop Supplier", null, null, "Addr", "City", "State", "560001", true, false);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var handler = new DeleteSupplierCommandHandler(_userRepository, _supplierRepository, _unitOfWork);
        var result = await handler.HandleAsync(new DeleteSupplierCommand(actor.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }
}
