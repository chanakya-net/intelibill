using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Models;
using Intelibill.Application.Features.Auth.Commands.ExternalLogin;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.ExternalLogin;

public class ExternalLoginCommandHandlerTests
{
    private readonly IValidator<ExternalLoginCommand> _validator = Substitute.For<IValidator<ExternalLoginCommand>>();
    private readonly IExternalAuthProvider _mockProvider = Substitute.For<IExternalAuthProvider>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ExternalLoginCommandHandler _handler;

    public ExternalLoginCommandHandlerTests()
    {
        _mockProvider.Provider.Returns(ExternalAuthProvider.Google);
        _handler = new ExternalLoginCommandHandler(_validator, new[] { _mockProvider }, _userRepository, _refreshTokenRepository, _tokenService, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<ExternalLoginCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_UnsupportedProvider_ReturnsError()
    {
        var command = new ExternalLoginCommand(ExternalAuthProvider.Facebook, "token123", null, null);
        // Handler with no providers
        var handler = new ExternalLoginCommandHandler(_validator, Array.Empty<IExternalAuthProvider>(), _userRepository, _refreshTokenRepository, _tokenService, _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UnsupportedProvider.Code);
    }

    [Fact]
    public async Task HandleAsync_InvalidProviderToken_ReturnsError()
    {
        var command = new ExternalLoginCommand(ExternalAuthProvider.Google, "invalidToken", null, null);
        _mockProvider.ValidateTokenAsync(command.Token, Arg.Any<CancellationToken>())
                     .Returns(Error.Unauthorized());

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_ValidNewUser_RegistersAndReturnsAuthResult()
    {
        var command = new ExternalLoginCommand(ExternalAuthProvider.Google, "validToken", "First", "Last");
        var userInfo = new ExternalUserInfo("provider123", "test@test.com", "First", "Last");
        _mockProvider.ValidateTokenAsync(command.Token, Arg.Any<CancellationToken>()).Returns(userInfo);

        _userRepository.GetByExternalLoginAsync(command.Provider, userInfo.ProviderKey, Arg.Any<CancellationToken>()).Returns((User?)null);
        _userRepository.GetByEmailAsync(userInfo.Email!, Arg.Any<CancellationToken>()).Returns((User?)null);

        _tokenService.GenerateAccessToken(Arg.Any<User>()).Returns(("accessToken", DateTimeOffset.UtcNow.AddMinutes(15)));
        var refreshToken = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "refreshToken", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(Arg.Any<Guid>()).Returns(refreshToken);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        await _userRepository.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
