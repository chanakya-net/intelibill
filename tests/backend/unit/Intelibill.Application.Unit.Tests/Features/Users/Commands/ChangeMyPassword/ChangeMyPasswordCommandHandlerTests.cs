using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.ChangeMyPassword;

public class ChangeMyPasswordCommandHandlerTests
{
    private readonly IValidator<ChangeMyPasswordCommand> _validator = Substitute.For<IValidator<ChangeMyPasswordCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ChangeMyPasswordCommandHandler _handler;

    public ChangeMyPasswordCommandHandlerTests()
    {
        _handler = new ChangeMyPasswordCommandHandler(
            _validator,
            _userRepository,
            _refreshTokenRepository,
            _passwordHasher,
            _unitOfWork);

        _validator.ValidateAsync(Arg.Any<ChangeMyPasswordCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsNotFoundError()
    {
        var command = new ChangeMyPasswordCommand(Guid.NewGuid(), "OldPass123!", "NewPass123!");
        _userRepository.GetByIdAsync(command.UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserNotFound.Code);
    }

    [Fact]
    public async Task HandleAsync_PasswordNotSet_ReturnsValidationError()
    {
        var user = User.CreateFromExternalProvider("external@test.com", "First", "Last");
        var command = new ChangeMyPasswordCommand(user.Id, "OldPass123!", "NewPass123!");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PasswordNotSet.Code);
    }

    [Fact]
    public async Task HandleAsync_InvalidCurrentPassword_ReturnsUnauthorizedError()
    {
        var user = User.CreateWithEmail("user@test.com", "old-hash", "First", "Last");
        var command = new ChangeMyPasswordCommand(user.Id, "WrongPass123!", "NewPass123!");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash!).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCurrentPassword.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_UpdatesPasswordAndRevokesTokens()
    {
        var user = User.CreateWithEmail("user@test.com", "old-hash", "First", "Last");
        var command = new ChangeMyPasswordCommand(user.Id, "OldPass123!", "NewPass123!");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash!).Returns(true);
        _passwordHasher.Hash(command.NewPassword).Returns("new-hash");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("new-hash", user.PasswordHash);

        _userRepository.Received(1).Update(user);
        await _refreshTokenRepository.Received(1).RevokeAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
