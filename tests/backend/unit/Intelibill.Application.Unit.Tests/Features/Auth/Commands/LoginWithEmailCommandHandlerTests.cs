using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.LoginWithEmail;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.LoginWithEmail;

public class LoginWithEmailCommandHandlerTests
{
    private readonly IValidator<LoginWithEmailCommand> _validator = Substitute.For<IValidator<LoginWithEmailCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LoginWithEmailCommandHandler _handler;

    public LoginWithEmailCommandHandlerTests()
    {
        _handler = new LoginWithEmailCommandHandler(_validator, _userRepository, _refreshTokenRepository, _passwordHasher, _tokenService, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<LoginWithEmailCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsInvalidCredentialsError()
    {
        var command = new LoginWithEmailCommand("test@test.com", "password123!");
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
    }

    [Fact]
    public async Task HandleAsync_InvalidPassword_ReturnsInvalidCredentialsError()
    {
        var command = new LoginWithEmailCommand("test@test.com", "WrongPass!");
        var user = User.CreateWithEmail(command.Email, "hashed", "First", "Last");
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsAuthResult()
    {
        var command = new LoginWithEmailCommand("test@test.com", "Pass123!");
        var user = User.CreateWithEmail(command.Email, "hashed", "First", "Last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        _tokenService.GenerateAccessToken(user).Returns(("accessToken", DateTimeOffset.UtcNow.AddMinutes(15)));

        var refreshToken = Domain.Entities.RefreshToken.Create(user.Id, "refreshToken", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(user.Id).Returns(refreshToken);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        Assert.Equal("refreshToken", result.Value.RefreshToken);
        Assert.Equal(user.Id, result.Value.User.Id);

        await _refreshTokenRepository.Received(1).AddAsync(refreshToken, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
