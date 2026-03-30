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
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbiddenError()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true);
        actor.AddShopMembership(actorMembership);

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "sales.updated@test.com", "Sales", "User", "+15551230000", "Manager", true);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
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

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "other.owner@test.com", "Other", "Owner", "+15551230000", "Manager", false);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
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

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "sales.manager@test.com", "Sales", "Manager", "+15551230000", "Manager", false);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, target.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Manager", result.Value.Role);
        Assert.Equal("Sales", target.FirstName);
        Assert.Equal("Manager", target.LastName);
        Assert.Equal(command.Email, target.Email);
        Assert.Equal(command.PhoneNumber, target.PhoneNumber);
        Assert.False(target.IsLoginEnabled);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenEmailAlreadyExists_ReturnsConflictError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        var targetMembership = ShopMembership.Create(shop.Id, target.Id, ShopRole.Staff, false);
        actor.AddShopMembership(actorMembership);
        target.AddShopMembership(targetMembership);

        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "sales.dup@test.com", "Sales", "User", "+15551230000", "Manager", true);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userRepository.ExistsByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.EmailAlreadyInUse.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenShopIdsProvided_RemovesMembershipForUncheckedShop()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop1 = Shop.Create("Shop 1", "Address", "City", "State", "560001", null, null, null);
        var shop2 = Shop.Create("Shop 2", "Address", "City", "State", "560001", null, null, null);

        var actorMembership1 = ShopMembership.Create(shop1.Id, actor.Id, ShopRole.Owner, true);
        var actorMembership2 = ShopMembership.Create(shop2.Id, actor.Id, ShopRole.Owner, false);
        actor.AddShopMembership(actorMembership1);
        actor.AddShopMembership(actorMembership2);

        var targetMembership1 = ShopMembership.Create(shop1.Id, target.Id, ShopRole.Staff, true);
        var targetMembership2 = ShopMembership.Create(shop2.Id, target.Id, ShopRole.Staff, false);
        target.AddShopMembership(targetMembership1);
        target.AddShopMembership(targetMembership2);

        // Provide only shop1 — shop2 membership should be removed
        var command = new EditShopUserCommand(actor.Id, shop1.Id, target.Id, "sales@test.com", "Sales", "User", "+15551231234", "SalesPerson", true, [shop1.Id]);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, target.Id, Arg.Any<CancellationToken>()).Returns(false);
        _shopRepository.GetMembershipsForUsersInShopsAsync(
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ShopMembership> { targetMembership1, targetMembership2 });

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        _shopRepository.Received(1).RemoveMembership(targetMembership2);
        _shopRepository.DidNotReceive().RemoveMembership(targetMembership1);
        await _shopRepository.DidNotReceive().AddMembershipAsync(Arg.Any<ShopMembership>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenShopIdsProvided_AddsMembershipForNewlyCheckedShop()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop1 = Shop.Create("Shop 1", "Address", "City", "State", "560001", null, null, null);
        var shop2 = Shop.Create("Shop 2", "Address", "City", "State", "560001", null, null, null);

        var actorMembership1 = ShopMembership.Create(shop1.Id, actor.Id, ShopRole.Owner, true);
        var actorMembership2 = ShopMembership.Create(shop2.Id, actor.Id, ShopRole.Owner, false);
        actor.AddShopMembership(actorMembership1);
        actor.AddShopMembership(actorMembership2);

        var targetMembership1 = ShopMembership.Create(shop1.Id, target.Id, ShopRole.Staff, true);
        target.AddShopMembership(targetMembership1);

        // Provide both shops — shop2 membership should be added
        var command = new EditShopUserCommand(actor.Id, shop1.Id, target.Id, "sales@test.com", "Sales", "User", "+15551231234", "SalesPerson", true, [shop1.Id, shop2.Id]);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, target.Id, Arg.Any<CancellationToken>()).Returns(false);
        _shopRepository.GetMembershipsForUsersInShopsAsync(
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<ShopMembership> { targetMembership1 });

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _shopRepository.Received(1).AddMembershipAsync(
            Arg.Is<ShopMembership>(m => m.ShopId == shop2.Id && m.UserId == target.Id),
            Arg.Any<CancellationToken>());
        _shopRepository.DidNotReceive().RemoveMembership(Arg.Any<ShopMembership>());
    }

    [Fact]
    public async Task HandleAsync_WhenShopIdsNull_DoesNotTouchMemberships()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var target = User.CreateWithPhone("+15551231234", "Sales", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var actorMembership = ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true);
        var targetMembership = ShopMembership.Create(shop.Id, target.Id, ShopRole.Staff, false);
        actor.AddShopMembership(actorMembership);
        target.AddShopMembership(targetMembership);

        // ShopIds is null (default) — membership reconciliation should be skipped
        var command = new EditShopUserCommand(actor.Id, shop.Id, target.Id, "sales@test.com", "Sales", "User", "+15551231234", "SalesPerson", true);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _userRepository.GetByIdWithDetailsAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, target.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new EditShopUserCommandHandler(_userRepository, _shopRepository, _unitOfWork);
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _shopRepository.DidNotReceive().GetMembershipsForUsersInShopsAsync(
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<CancellationToken>());
        _shopRepository.DidNotReceive().RemoveMembership(Arg.Any<ShopMembership>());
        await _shopRepository.DidNotReceive().AddMembershipAsync(Arg.Any<ShopMembership>(), Arg.Any<CancellationToken>());
    }
}
