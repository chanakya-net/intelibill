using ErrorOr;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Queries;

public class GetMyShopsQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly GetMyShopsQueryHandler _handler;

    public GetMyShopsQueryHandlerTests()
    {
        _handler = new GetMyShopsQueryHandler(_userRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenUserNotFound_ReturnsError()
    {
        _userRepository.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.HandleAsync(new GetMyShopsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasNoMemberships_ReturnsEmptyList()
    {
        var user = User.CreateWithEmail("user@test.com", "pass", "Test", "User");
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var result = await _handler.HandleAsync(new GetMyShopsQuery(user.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }
}
