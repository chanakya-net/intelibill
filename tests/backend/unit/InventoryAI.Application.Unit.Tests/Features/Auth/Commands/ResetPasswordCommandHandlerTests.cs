using FluentValidation;
using InventoryAI.Application.Common.Errors;
using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Application.Features.Auth.Commands.ResetPassword;
using InventoryAI.Domain.Entities;
using InventoryAI.Domain.Interfaces;
using InventoryAI.Domain.Interfaces.Repositories;
using NSubstitute;

namespace InventoryAI.Application.Unit.Tests.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandlerTests
{
    private readonly IValidator<ResetPasswordCommand> _validator = Substitute.For<IValidator<ResetPasswordCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ResetPasswordCommandHandler _handler;

    public ResetPasswordCommandHandlerTests()
    {
        _handler = new ResetPasswordCommandHandler(_validator, _userRepository, _passwordResetTokenRepository, _refreshTokenRepository, _passwordHasher, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<ResetPasswordCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsError()
    {
        var command = new ResetPasswordCommand("test@test.com", "token123", "NewPass123!");
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidPasswordResetToken.Code);
    }

    [Fact]
    public async Task HandleAsync_NoValidResetToken_ReturnsError()
    {
        var command = new ResetPasswordCommand("test@test.com", "token123", "NewPass123!");
        var user = User.CreateWithEmail(command.Email, "hash", "first", "last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordResetTokenRepository.GetValidByUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns((PasswordResetToken?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidPasswordResetToken.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_ResetsPasswordAndRevokesRefreshTokens()
    {
        // The handler hashes the raw token with SHA256; supply a matching hash
        var rawToken = "token123";
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
        var command = new ResetPasswordCommand("test@test.com", rawToken, "NewPass123!");
        var user = User.CreateWithEmail(command.Email, "oldHash", "first", "last");
        var token = PasswordResetToken.Create(user.Id, hash);

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordResetTokenRepository.GetValidByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(token);
        _passwordHasher.Hash(command.NewPassword).Returns("newHashedPassword");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("newHashedPassword", user.PasswordHash);
        Assert.True(token.IsUsed);

        await _refreshTokenRepository.Received(1).RevokeAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
