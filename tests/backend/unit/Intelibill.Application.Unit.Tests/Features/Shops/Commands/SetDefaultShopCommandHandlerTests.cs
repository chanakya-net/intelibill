using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.SetDefaultShop;

public class SetDefaultShopCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IAuthSessionRepository _sessions = Substitute.For<IAuthSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SetDefaultShopCommandHandler Handler(User user, long? lockedVersion = null)
    {
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _sessions.BeginAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _sessions.CommitAsync(Arg.Any<CancellationToken>()).Returns(true);
        _sessions.LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            new UserAuthority(user.Id, lockedVersion ?? user.SecurityVersion, true, user.Email is not null, user.IsEmailVerified));
        return new SetDefaultShopCommandHandler(_userRepository, _sessions, _unitOfWork);
    }

    private static (User User, Shop First, Shop Second) TwoShops()
    {
        // Verified, because an account that signs in with an unproved email cannot hold a session
        // at all and would be refused before the preference is even considered.
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "One");

        var firstShop = Shop.Create("First", "Address", "City", "State", "560001", null, null, null);
        var firstMembership = ShopMembership.Create(firstShop.Id, user.Id, ShopRole.Owner, true);
        firstShop.AddMembership(firstMembership);
        user.AddShopMembership(firstMembership);

        var secondShop = Shop.Create("Second", "Address", "City", "State", "560002", null, null, null);
        var secondMembership = ShopMembership.Create(secondShop.Id, user.Id, ShopRole.Manager, false);
        secondShop.AddMembership(secondMembership);
        user.AddShopMembership(secondMembership);

        return (user, firstShop, secondShop);
    }

    [Fact]
    public async Task HandleAsync_MultipleShops_SetsRequestedShopAsDefault()
    {
        var (user, firstShop, secondShop) = TwoShops();

        var result = await Handler(user).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id, user.SecurityVersion),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(secondShop.Id, result.Value.Shops.Single(s => s.IsDefault).ShopId);
        Assert.False(result.Value.Shops.Single(s => s.ShopId == firstShop.Id).IsDefault);
    }

    /// <summary>
    /// Saving a default is a stored preference, not a scope change. The device that saved it stays
    /// in the shop its operator is working in, and gets no new token for having saved it.
    /// </summary>
    [Fact]
    public async Task HandleAsync_LeavesTheRequestingDeviceInTheShopItWasAlreadyIn()
    {
        var (user, firstShop, secondShop) = TwoShops();

        var result = await Handler(user).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id, user.SecurityVersion),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(firstShop.Id, result.Value.ActiveShopId);
    }

    /// <summary>
    /// Clearing the old default and setting the new one are two writes, because the partial unique
    /// index cannot see both at once. They commit together, so the account is never left with no
    /// default.
    /// </summary>
    [Fact]
    public async Task HandleAsync_LocksTheAccountAndCommitsBothWritesAsOneTransaction()
    {
        var (user, firstShop, secondShop) = TwoShops();

        _ = await Handler(user).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id, user.SecurityVersion),
            CancellationToken.None);

        await _sessions.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        await _sessions.Received(1).LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sessions.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A preference write must not outrun a security change. If the account moved to a new version
    /// while the request was in flight, the write is refused rather than landing on behalf of a
    /// session that has already been invalidated.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheAccountMovedSinceTheTokenWasMinted_RefusesTheWrite()
    {
        var (user, firstShop, secondShop) = TwoShops();

        var result = await Handler(user, lockedVersion: user.SecurityVersion + 1).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id, user.SecurityVersion),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.SessionInvalidated.Code, result.FirstError.Code);
        Assert.True(user.ShopMemberships.Single(sm => sm.ShopId == firstShop.Id).IsDefault);
    }
}
