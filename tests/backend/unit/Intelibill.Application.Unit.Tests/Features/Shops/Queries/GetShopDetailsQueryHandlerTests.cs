using ErrorOr;
using Intelibill.Application.Features.Shops.Queries.GetShopDetails;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Queries;

public class GetShopDetailsQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly GetShopDetailsQueryHandler _handler;

    public GetShopDetailsQueryHandlerTests()
    {
        _handler = new GetShopDetailsQueryHandler(_userRepository, _shopRepository, _bankAccountRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenUserNotFound_ReturnsError()
    {
        _userRepository.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.HandleAsync(new GetShopDetailsQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipNotFound_ReturnsError()
    {
        var user = User.CreateWithEmail("user@test.com", "pass", "Test", "User");
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var result = await _handler.HandleAsync(new GetShopDetailsQuery(user.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_WhenShopNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "Test", "User");
        var membership = ShopMembership.Create(shopId, userId, ShopRole.Owner, false);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var result = await _handler.HandleAsync(new GetShopDetailsQuery(userId, shopId), CancellationToken.None);

        Assert.True(result.IsError);
    }
}
