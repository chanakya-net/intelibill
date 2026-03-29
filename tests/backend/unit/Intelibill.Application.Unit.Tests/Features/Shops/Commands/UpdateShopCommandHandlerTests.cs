using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.UpdateShop;

public class UpdateShopCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenUserIsOwner_UpdatesShopAndSaves()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Old", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        var command = new UpdateShopCommand(
            user.Id,
            shop.Id,
            "  Main Shop  ",
            "  42 MG Road  ",
            "  Bengaluru  ",
            "  Karnataka  ",
            "  560001  ",
            "  Chandra  ",
            "  9876543210  ",
            "  27AAPFU0939F1ZV  ");

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var handler = new UpdateShopCommandHandler(
            BuildValidValidator(),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shop.Id, result.Value.ShopId);
        Assert.Equal("Main Shop", result.Value.Name);
        Assert.Equal("42 MG Road", result.Value.Address);
        Assert.Equal("Bengaluru", result.Value.City);
        Assert.Equal("Karnataka", result.Value.State);
        Assert.Equal("560001", result.Value.Pincode);
        Assert.Equal("Chandra", result.Value.ContactPerson);
        Assert.Equal("9876543210", result.Value.MobileNumber);
        Assert.Equal("27AAPFU0939F1ZV", result.Value.GstNumber);

        _shopRepository.Received(1).Update(shop);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenUserMissing_ReturnsUserNotFound()
    {
        var command = new UpdateShopCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main",
            "Address",
            "City",
            "State",
            "560001",
            null,
            null,
            null);

        _userRepository.GetByIdWithDetailsAsync(command.UserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = new UpdateShopCommandHandler(
            BuildValidValidator(),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.UserNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipMissing_ReturnsMembershipNotFound()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var command = new UpdateShopCommand(
            user.Id,
            Guid.NewGuid(),
            "Main",
            "Address",
            "City",
            "State",
            "560001",
            null,
            null,
            null);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var handler = new UpdateShopCommandHandler(
            BuildValidValidator(),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.MembershipNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsNotOwner_ReturnsForbidden()
    {
        var user = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, false);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        var command = new UpdateShopCommand(
            user.Id,
            shop.Id,
            "Main",
            "Address",
            "City",
            "State",
            "560001",
            null,
            null,
            null);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var handler = new UpdateShopCommandHandler(
            BuildValidValidator(),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.UserIsNotOwner", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenShopMissing_ReturnsShopNotFound()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shopId = Guid.NewGuid();
        var membership = ShopMembership.Create(shopId, user.Id, ShopRole.Owner, true);
        user.AddShopMembership(membership);

        var command = new UpdateShopCommand(
            user.Id,
            shopId,
            "Main",
            "Address",
            "City",
            "State",
            "560001",
            null,
            null,
            null);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var handler = new UpdateShopCommandHandler(
            BuildValidValidator(),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.ShopNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenNameIsBlank_ReturnsValidationError()
    {
        var command = new UpdateShopCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "   ",
            "Address",
            "City",
            "State",
            "560001",
            null,
            null,
            null);

        var handler = new UpdateShopCommandHandler(
            BuildInvalidValidator("Name", "Name is required."),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Name", result.FirstError.Code);
        await _userRepository.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenGstNumberIsInvalid_ReturnsValidationError()
    {
        var command = new UpdateShopCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main",
            "Address",
            "City",
            "State",
            "560001",
            null,
            null,
            "123");

        var handler = new UpdateShopCommandHandler(
            BuildInvalidValidator("GstNumber", "GST number must be a valid Indian GSTIN."),
            _userRepository,
            _shopRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("GstNumber", result.FirstError.Code);
        await _userRepository.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static IValidator<UpdateShopCommand> BuildValidValidator()
    {
        var validator = Substitute.For<IValidator<UpdateShopCommand>>();
        validator.ValidateAsync(Arg.Any<UpdateShopCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        return validator;
    }

    private static IValidator<UpdateShopCommand> BuildInvalidValidator(string propertyName, string errorMessage)
    {
        var validator = Substitute.For<IValidator<UpdateShopCommand>>();
        validator.ValidateAsync(Arg.Any<UpdateShopCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure(propertyName, errorMessage)]));
        return validator;
    }
}
