using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.Commands.EditShopUser;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.EditShopUser;

public class EditShopUserCommandHandlerTests
{
    private readonly IValidator<EditShopUserCommand> _validator = Substitute.For<IValidator<EditShopUserCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public EditShopUserCommandHandlerTests()
    {
        _validator.ValidateAsync(Arg.Any<EditShopUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbiddenError()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true);
        actor.AddShopMembership(actorMembership);

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "Sales", "User", "+15551230000", "Manager", true);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new EditShopUserCommandHandler(_validator, _userRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.UserIsNotOwner.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenTargetIsOwner_ReturnsCannotModifyOwnerError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var target = User.CreateWithPhone("+15551231234", "Other", "Owner");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        var targetMembership = ShopMembership.Create(shop.Id, target.Id, ShopRole.Owner, false);
        actor.AddShopMembership(actorMembership);
        target.AddShopMembership(targetMembership);

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "Other", "Owner", "+15551230000", "Manager", false);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        var handler = new EditShopUserCommandHandler(_validator, _userRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Users.CannotModifyOwner.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesUserRoleAndLoginStatus()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        var targetMembership = ShopMembership.Create(shop.Id, target.Id, ShopRole.Staff, false);
        actor.AddShopMembership(actorMembership);
        target.AddShopMembership(targetMembership);

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "Sales", "Manager", "+15551230000", "Manager", false);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, target.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new EditShopUserCommandHandler(_validator, _userRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Manager", result.Value.Role);
        Assert.Equal("Sales", target.FirstName);
        Assert.Equal("Manager", target.LastName);
        Assert.Equal(command.PhoneNumber, target.PhoneNumber);
        Assert.False(target.IsLoginEnabled);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
