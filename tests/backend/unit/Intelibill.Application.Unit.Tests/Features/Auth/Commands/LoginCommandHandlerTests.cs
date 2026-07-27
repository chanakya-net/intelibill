using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.Login;

public class LoginCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(_userRepository, _refreshTokenRepository, _passwordHasher, _tokenService, _unitOfWork);
    }

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
        var user = User.CreateWithEmail(command.Identifier, "hashed", "First", "Last");
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
        var user = User.CreateWithEmail("test@test.com", "hashed", "First", "Last");

        _userRepository.GetByEmailAsync("test@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        _tokenService.GenerateAccessTokenAsync(user, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(("accessToken", DateTimeOffset.UtcNow.AddMinutes(15)));

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

    [Fact]
    public async Task HandleAsync_WhenLoginDisabled_ReturnsUserLoginDisabledError()
    {
        var command = new LoginCommand("test@test.com", "Pass123!");
        var user = User.CreateWithEmail("test@test.com", "hashed", "First", "Last");
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
        var user = User.CreateWithEmail("test.user@test.com", "hashed", "First", "Last");
        _userRepository.GetByEmailAsync("test.user@test.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.Password, user.PasswordHash!).Returns(true);
        _tokenService.GenerateAccessTokenAsync(user, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(("accessToken", DateTimeOffset.UtcNow.AddMinutes(15)));
        _tokenService.CreateRefreshToken(user.Id).Returns(Domain.Entities.RefreshToken.Create(user.Id, "refreshToken", DateTimeOffset.UtcNow.AddDays(7)));

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
        _tokenService.GenerateAccessTokenAsync(user, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(("accessToken", DateTimeOffset.UtcNow.AddMinutes(15)));
        _tokenService.CreateRefreshToken(user.Id).Returns(Domain.Entities.RefreshToken.Create(user.Id, "refreshToken", DateTimeOffset.UtcNow.AddDays(7)));

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
}
