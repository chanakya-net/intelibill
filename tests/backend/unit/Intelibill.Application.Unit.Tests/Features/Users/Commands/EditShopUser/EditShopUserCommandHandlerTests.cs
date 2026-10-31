using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.Commands.EditShopUser;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.EditShopUser;

/// <summary>
/// The legacy payload rewrote the target's global account. These tests replace the ones that
/// asserted that behaviour: the endpoint now refuses the whole payload, so no part of the global
/// identity can be reached through shop administration.
/// </summary>
public class EditShopUserCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    private static EditShopUserCommand Command(Guid actorId, Guid shopId, Guid targetId) =>
        new(actorId, shopId, targetId, "target@test.com", "Seized", "Identity", "+15551230000", "Staff", false);

    [Fact]
    public async Task HandleAsync_WhenOwnerSubmitsIdentityPayload_RefusesAndLeavesAccountUntouched()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var target = User.CreateWithEmail("target@test.com", "target-hash", "Target", "Member");
        target.AddShopMembership(ShopMembership.Create(shop.Id, target.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        var handler = new EditShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(Command(owner.Id, shop.Id, target.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Users.MembershipOperationRequired.Code, result.FirstError.Code);
        Assert.Equal("target@test.com", target.Email);
        Assert.Equal("target-hash", target.PasswordHash);
        Assert.Equal("Target", target.FirstName);
        Assert.True(target.IsLoginEnabled);
        _userRepository.DidNotReceive().Update(Arg.Any<User>());
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new EditShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(Command(actor.Id, shop.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.UserIsNotOwner.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenActorHasNoMembershipInTheActiveShop_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("outsider@test.com", "hash", "Outsider", "User");
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new EditShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(Command(actor.Id, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSuspendedOwnerMembership_ReturnsForbidden()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        membership.ChangeAuthority(ShopRole.Owner, MembershipStatus.Suspended);
        owner.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        var handler = new EditShopUserCommandHandler(_userRepository);
        var result = await handler.HandleAsync(Command(owner.Id, shop.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }
}
