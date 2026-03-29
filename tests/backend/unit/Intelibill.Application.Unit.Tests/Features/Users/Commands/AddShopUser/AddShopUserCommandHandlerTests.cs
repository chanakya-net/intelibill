using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.Commands.AddShopUser;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.AddShopUser;

public class AddShopUserCommandHandlerTests
{
    private readonly IValidator<AddShopUserCommand> _validator = Substitute.For<IValidator<AddShopUserCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public AddShopUserCommandHandlerTests()
    {
        _validator.ValidateAsync(Arg.Any<AddShopUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbiddenError()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true);
        actor.AddShopMembership(actorMembership);

        var command = new AddShopUserCommand(actor.Id, shop.Id, "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "SalesPerson");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new AddShopUserCommandHandler(_validator, _userRepository, _shopRepository, _passwordHasher, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.UserIsNotOwner.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenRoleInvalid_ReturnsValidationError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        actor.AddShopMembership(actorMembership);

        var command = new AddShopUserCommand(actor.Id, shop.Id, "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "Owner");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new AddShopUserCommandHandler(_validator, _userRepository, _shopRepository, _passwordHasher, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Users.RoleNotSupported.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_AddsSalesPersonMember()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        shop.AddMembership(actorMembership);
        actor.AddShopMembership(actorMembership);

        var command = new AddShopUserCommand(actor.Id, shop.Id, "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "SalesPerson");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(false);
        _passwordHasher.Hash(command.Password).Returns("hashed-pass");

        var handler = new AddShopUserCommandHandler(_validator, _userRepository, _shopRepository, _passwordHasher, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("SalesPerson", result.Value.Role);
        Assert.Equal(command.FirstName, result.Value.FirstName);
        Assert.Equal(command.LastName, result.Value.LastName);
        Assert.Equal(command.PhoneNumber, result.Value.PhoneNumber);

        await _userRepository.Received(1).AddAsync(Arg.Is<User>(u =>
            u.FirstName == command.FirstName
            && u.LastName == command.LastName
            && u.PhoneNumber == command.PhoneNumber
            && u.ShopMemberships.Any(sm => sm.ShopId == shop.Id && sm.Role == ShopRole.Staff)), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
