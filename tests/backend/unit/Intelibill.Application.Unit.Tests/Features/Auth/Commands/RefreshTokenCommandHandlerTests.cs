using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RefreshToken;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        _handler = new RefreshTokenCommandHandler(_refreshTokenRepository, _tokenService, _unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_TokenNotFound_ReturnsError()
    {
        var command = new RefreshTokenCommand("invalid");
        _refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, Arg.Any<CancellationToken>())
                               .Returns((Domain.Entities.RefreshToken?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidRefreshToken.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_RevokesOldAndPersistsNewToken()
    {
        var command = new RefreshTokenCommand("valid");
        var token = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "valid", DateTimeOffset.UtcNow.AddDays(7));

        _refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, Arg.Any<CancellationToken>())
                               .Returns(token);

        _tokenService.GenerateAccessTokenAsync(Arg.Any<User>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(("newAccessToken", DateTimeOffset.UtcNow.AddMinutes(15)));
        var newRefreshToken = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "newRefreshToken", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(Arg.Any<Guid>()).Returns(newRefreshToken);

        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        typeof(Domain.Entities.RefreshToken).GetProperty(nameof(Domain.Entities.RefreshToken.User))!.SetValue(token, user);
        token.BindSecurityVersion(user.SecurityVersion);
        var result = await _handler.HandleAsync(command, CancellationToken.None);
        Assert.False(result.IsError);
        Assert.True(token.IsRevoked);
        await _refreshTokenRepository.Received(1).AddAsync(newRefreshToken, Arg.Any<CancellationToken>());
    }
}
