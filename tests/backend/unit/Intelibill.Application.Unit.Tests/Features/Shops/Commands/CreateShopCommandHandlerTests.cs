using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.CreateShop;

public class CreateShopCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CreateShopCommandHandler Handler() => new(
        _userRepository,
        _shopRepository,
        _supplierRepository,
        _sessions,
        _unitOfWork);

    [Fact]
    public async Task HandleAsync_FirstShop_AssignsDefaultAndMovesTheRequestingSessionIntoIt()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var sessionId = Guid.NewGuid();
        var command = new CreateShopCommand(
            user.Id,
            sessionId,
            "Main Shop",
            "42 MG Road",
            "Bengaluru",
            "Karnataka",
            "560001",
            "Chandra",
            "9876543210",
            "27AAPFU0939F1ZV");

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _sessions.RescopeAsync(user.Id, sessionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => AuthResults.Scope(call.ArgAt<Guid>(2)));

        var result = await Handler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.ActiveShopId);
        Assert.True(user.ShopMemberships.Single().IsDefault);

        await _shopRepository.Received(1).AddAsync(
            Arg.Is<Shop>(s =>
                s.Name == "Main Shop"
                && s.Address == "42 MG Road"
                && s.City == "Bengaluru"
                && s.State == "Karnataka"
                && s.Pincode == "560001"
                && s.ContactPerson == "Chandra"
                && s.MobileNumber == "9876543210"
                && s.GstNumber == "27AAPFU0939F1ZV"),
            Arg.Any<CancellationToken>());

        await _supplierRepository.Received(1).AddAsync(Arg.Is<Supplier>(s => s.IsSystem), Arg.Any<CancellationToken>());

        // The shop is committed before the scope moves, so the caller never loses a created shop to
        // a failed rescope.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sessions.Received(1).RescopeAsync(
            user.Id,
            sessionId,
            Arg.Is<Guid>(id => id == user.ShopMemberships.Single().ShopId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creating a shop must not hand out a new refresh credential: the device that asked keeps the
    /// one its sign-in earned.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ReturnsAnAccessScopeWithNoRefreshCredential()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _sessions.RescopeAsync(user.Id, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => AuthResults.Scope(call.ArgAt<Guid>(2)));

        var result = await Handler().HandleAsync(
            new CreateShopCommand(user.Id, Guid.NewGuid(), "Main", "A", "C", "S", "560001", null, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.IsType<Application.Features.Auth.DTOs.AccessScopeResult>(result.Value);
        await _sessions.DidNotReceive().StartAsync(
            Arg.Any<Application.Features.Auth.DTOs.AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
