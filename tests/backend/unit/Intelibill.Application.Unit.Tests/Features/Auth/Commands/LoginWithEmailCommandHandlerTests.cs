using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.LoginWithEmail;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.LoginWithEmail;

public class LoginWithEmailCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly LoginWithEmailCommandHandler _handler;

    public LoginWithEmailCommandHandlerTests()
    {
        _handler = new LoginWithEmailCommandHandler(_userRepository, _passwordHasher, _sessions, TimeProvider.System);
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
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hashed", "First", "Last");
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
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hashed", "First", "Last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        _sessions.StartAsync(Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(AuthResults.For(user));

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        Assert.Equal("refreshToken", result.Value.RefreshToken);
        Assert.Equal(user.Id, result.Value.User.Id);

        await _sessions.Received(1).StartAsync(
            Arg.Is<AuthenticationProof>(x =>
                x.UserId == user.Id &&
                x.Credential == CredentialKind.Password &&
                x.VerifiedSecurityVersion == user.SecurityVersion),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenLoginDisabled_ReturnsUserLoginDisabledError()
    {
        var command = new LoginWithEmailCommand("test@test.com", "Pass123!");
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hashed", "First", "Last");
        user.SetLoginEnabled(false);

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserLoginDisabled.Code);
    }
}
