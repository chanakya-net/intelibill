using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.Commands.AddShopUser;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.AddShopUser;

public class AddShopUserCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbiddenError()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true);
        actor.AddShopMembership(actorMembership);

        var command = new AddShopUserCommand(actor.Id, [shop.Id], "sales@test.com", "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "Staff");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new AddShopUserCommandHandler(_userRepository);
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

        var command = new AddShopUserCommand(actor.Id, [shop.Id], "sales@test.com", "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "Owner");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new AddShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Users.RoleNotSupported.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenOwnerCreatesUnprovedIdentity_RequiresInvitation()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        shop.AddMembership(actorMembership);
        actor.AddShopMembership(actorMembership);

        var command = new AddShopUserCommand(actor.Id, [shop.Id], "sales@test.com", "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "Staff");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        var handler = new AddShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Auth.InvitationRequired", result.FirstError.Code);
        await _userRepository.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenEmailAlreadyExists_ReturnsConflictError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        actor.AddShopMembership(actorMembership);

        var command = new AddShopUserCommand(actor.Id, [shop.Id], "sales@test.com", "Sales", "User", "+15551231234", "Pass1234!", "Pass1234!", "Staff");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        var handler = new AddShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Auth.InvitationRequired", result.FirstError.Code);
    }
}
