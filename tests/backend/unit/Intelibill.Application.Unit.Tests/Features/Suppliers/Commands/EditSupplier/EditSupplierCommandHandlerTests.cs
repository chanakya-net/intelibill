using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Suppliers.Commands.EditSupplier;

public class EditSupplierCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenSupplierNotFound_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Supplier?)null);

        var handler = new EditSupplierCommandHandler(_userRepository, _supplierRepository, _unitOfWork);
        var result = await handler.HandleAsync(new EditSupplierCommand(
            actor.Id,
            shop.Id,
            Guid.NewGuid(),
            "Updated",
            null,
            null,
            "Address",
            "City",
            "State",
            "560001",
            true,
            false), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesSupplier()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var supplier = Supplier.Create(
            actor.Id,
            "Old",
            "Person",
            "+919999999999",
            "Old Address",
            "City",
            "State",
            "560001",
            true,
            false);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var handler = new EditSupplierCommandHandler(_userRepository, _supplierRepository, _unitOfWork);
        var result = await handler.HandleAsync(new EditSupplierCommand(
            actor.Id,
            shop.Id,
            supplier.Id,
            "Updated",
            "New Person",
            "+918888888888",
            "42 MG Road",
            "Bengaluru",
            "Karnataka",
            "560002",
            false,
            true), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Updated", result.Value.Name);
        Assert.True(result.Value.IsPreferred);
        Assert.False(result.Value.IsActive);
        _supplierRepository.Received(1).Update(Arg.Is<Supplier>(s =>
            s.Name == "Updated"
            && s.Pin == "560002"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSystemSupplier_ReturnsCannotModifySystemSupplier()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(systemSupplier.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);

        var handler = new EditSupplierCommandHandler(_userRepository, _supplierRepository, _unitOfWork);
        var result = await handler.HandleAsync(new EditSupplierCommand(
            actor.Id,
            shop.Id,
            systemSupplier.Id,
            "Updated",
            null,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            true,
            false), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.CannotModifySystemSupplier.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
