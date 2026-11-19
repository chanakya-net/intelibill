using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Suppliers.Commands.AddSupplier;

public class AddSupplierCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ILiveSessionAuthority _authority = Substitute.For<ILiveSessionAuthority>();
    private readonly IShopLicenceFence _fence = Substitute.For<IShopLicenceFence>();

    public AddSupplierCommandHandlerTests()
    {
        _authority.IsValidAsync(Arg.Any<SessionClaims>(), Arg.Any<CancellationToken>()).Returns(true);
        _fence.EnterAsync(Arg.Any<Guid>(), LicenceAction.OperationalCapacity, Arg.Any<CancellationToken>())
            .Returns(call => new ShopLicenceLease(call.Arg<Guid>(), null, null, null, new(LicenceOutcome.Allowed, true)));
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new AddSupplierCommand(
            new SessionClaims(actor.Id, Guid.NewGuid(), 0, 0, shop.Id, null),
            "Supplier",
            "Contact",
            "+919999999999",
            "Address",
            "City",
            "State",
            "560001",
            true,
            false), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.UserIsNotOwner.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_AddsSupplier()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new AddSupplierCommand(
            new SessionClaims(actor.Id, Guid.NewGuid(), 0, 0, shop.Id, null),
            "  Fresh Foods  ",
            "  Ramesh  ",
            "+919999999999",
            "  42 MG Road  ",
            "  Bengaluru  ",
            "  Karnataka  ",
            "  560001  ",
            true,
            true), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Fresh Foods", result.Value.Name);
        Assert.True(result.Value.IsPreferred);

        await _supplierRepository.Received(1).AddAsync(Arg.Is<Supplier>(s =>
            s.ShopId == shop.Id
            && s.Name == "Fresh Foods"
            && s.City == "Bengaluru"
            && s.IsPreferred), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAndCommitAsync(Arg.Any<CancellationToken>());
    }
    private AddSupplierCommandHandler CreateHandler() =>
        new(_userRepository, _supplierRepository, new DefinitionCreationTransaction(_unitOfWork, _authority, _fence));
}
