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
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_FirstShop_AssignsDefaultAndReturnsActiveShop()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var command = new CreateShopCommand(
            user.Id,
            "Main Shop",
            "42 MG Road",
            "Bengaluru",
            "Karnataka",
            "560001",
            "Chandra",
            "9876543210",
            "27AAPFU0939F1ZV");

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<Guid?>())
            .Returns(("access-token", DateTimeOffset.UtcNow.AddMinutes(15)));

        var refreshToken = Domain.Entities.RefreshToken.Create(user.Id, "refresh-token", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(user.Id).Returns(refreshToken);

        var handler = new CreateShopCommandHandler(
            _userRepository,
            _shopRepository,
            _refreshTokenRepository,
            _tokenService,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.ActiveShopId);
        var shops = result.Value.Shops;
        Assert.NotNull(shops);
        Assert.Single(shops!);
        Assert.True(shops[0].IsDefault);

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

        await _refreshTokenRepository.Received(1).AddAsync(refreshToken, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAddressBlank_ReturnsAddressRequired()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var command = new CreateShopCommand(user.Id, "Main Shop", "   ", "Bengaluru", "Karnataka", "560001", null, null, null);

        var handler = new CreateShopCommandHandler(
            _userRepository,
            _shopRepository,
            _refreshTokenRepository,
            _tokenService,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.AddressRequired", result.FirstError.Code);
        await _shopRepository.DidNotReceive().AddAsync(Arg.Any<Shop>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenGstNumberInvalid_ReturnsValidationError()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var command = new CreateShopCommand(user.Id, "Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, "123");

        var handler = new CreateShopCommandHandler(
            _userRepository,
            _shopRepository,
            _refreshTokenRepository,
            _tokenService,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.GstNumberInvalid", result.FirstError.Code);
        await _shopRepository.DidNotReceive().AddAsync(Arg.Any<Shop>(), Arg.Any<CancellationToken>());
    }
}