using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.Login;

public class LoginCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(_userRepository, _passwordHasher, _sessions, TimeProvider.System);
    }

    private void StartsASession(User user) =>
        _sessions.StartAsync(Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(AuthResults.For(user));

    [Fact]
    public async Task HandleAsync_EmailNotFound_ReturnsInvalidCredentialsError()
    {
        var command = new LoginCommand("  TEST@TEST.COM  ", "password123!");
        _userRepository.GetByEmailAsync("test@test.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
    }

    [Fact]
    public async Task HandleAsync_InvalidPassword_ReturnsInvalidCredentialsError()
    {
        var command = new LoginCommand("test@test.com", "WrongPass!");
        var user = VerifiedUsers.CreateWithEmail(command.Identifier, "hashed", "First", "Last");
        _userRepository.GetByEmailAsync("test@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidEmailCredentials_ReturnsAuthResult()
    {
        var command = new LoginCommand("test@test.com", "Pass123!");
        var user = VerifiedUsers.CreateWithEmail("test@test.com", "hashed", "First", "Last");

        _userRepository.GetByEmailAsync("test@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        StartsASession(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        Assert.Equal("refreshToken", result.Value.RefreshToken);
        Assert.Equal(user.Id, result.Value.User.Id);

        // The proof names the version the password was checked against, which is what lets session
        // creation refuse it if a reset commits in between.
        await _sessions.Received(1).StartAsync(
            Arg.Is<AuthenticationProof>(p =>
                p.UserId == user.Id &&
                p.Credential == CredentialKind.Password &&
                p.VerifiedSecurityVersion == user.SecurityVersion),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenLoginDisabled_ReturnsUserLoginDisabledError()
    {
        var command = new LoginCommand("test@test.com", "Pass123!");
        var user = VerifiedUsers.CreateWithEmail("test@test.com", "hashed", "First", "Last");
        user.SetLoginEnabled(false);

        _userRepository.GetByEmailAsync("test@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserLoginDisabled.Code);
    }

    [Fact]
    public async Task HandleAsync_TrimsAndLowercasesEmailBeforeLookup()
    {
        var command = new LoginCommand("  TEST.USER@TEST.COM  ", "Pass123!");
        var user = VerifiedUsers.CreateWithEmail("test.user@test.com", "hashed", "First", "Last");
        _userRepository.GetByEmailAsync("test.user@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        StartsASession(user);

        _ = await _handler.HandleAsync(command, CancellationToken.None);

        await _userRepository.Received(1).GetByEmailAsync("test.user@test.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidPhoneCredentials_ReturnsAuthResult()
    {
        var command = new LoginCommand(" +911234567890 ", "Pass123!");
        var user = User.CreateWithPhone("+911234567890", "First", "Last");
        user.UpdatePassword("hashed");

        _userRepository.GetByPhoneAsync("+911234567890", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        StartsASession(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        await _userRepository.Received(1).GetByPhoneAsync("+911234567890", Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PhoneIdentifier_DoesNotNormalizeBeforeLookup()
    {
        var command = new LoginCommand("  9876543210  ", "Pass123!");
        _userRepository.GetByPhoneAsync("9876543210", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
        await _userRepository.Received(1).GetByPhoneAsync("9876543210", Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByPhoneAsync("+919876543210", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PhoneWrongPassword_ReturnsInvalidCredentialsError()
    {
        var command = new LoginCommand("+911234567890", "WrongPass!");
        var user = User.CreateWithPhone("+911234567890", "First", "Last");
        user.UpdatePassword("hashed");
        _userRepository.GetByPhoneAsync("+911234567890", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
    }

    [Fact]
    public async Task HandleAsync_PhoneUserWithoutPasswordHash_ReturnsInvalidCredentialsError()
    {
        var command = new LoginCommand("+911234567890", "Pass123!");
        var user = User.CreateWithPhone("+911234567890", "First", "Last");
        _userRepository.GetByPhoneAsync("+911234567890", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCredentials.Code);
    }

    [Fact]
    public async Task HandleAsync_PhoneUserLoginDisabled_ReturnsUserLoginDisabledError()
    {
        var command = new LoginCommand("+911234567890", "Pass123!");
        var user = User.CreateWithPhone("+911234567890", "First", "Last");
        user.UpdatePassword("hashed");
        user.SetLoginEnabled(false);
        _userRepository.GetByPhoneAsync("+911234567890", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserLoginDisabled.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenCredentialsFail_NeverReachesSessionCreation()
    {
        var command = new LoginCommand("test@test.com", "WrongPass!");
        var user = VerifiedUsers.CreateWithEmail(command.Identifier, "hashed", "First", "Last");
        _userRepository.GetByEmailAsync("test@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(false);

        _ = await _handler.HandleAsync(command, CancellationToken.None);

        await _sessions.DidNotReceive().StartAsync(
            Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
