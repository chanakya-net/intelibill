using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.ChangeMyPassword;

public class ChangeMyPasswordCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IAuthSessionRepository _sessions = Substitute.For<IAuthSessionRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ChangeMyPasswordCommandHandler _handler;

    public ChangeMyPasswordCommandHandlerTests()
    {
        _sessions.BeginAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _sessions.CommitAsync(Arg.Any<CancellationToken>()).Returns(true);
        _handler = new ChangeMyPasswordCommandHandler(
            _userRepository,
            _sessions,
            _passwordHasher,
            TimeProvider.System);
    }

    private void AccountIs(User? user, long? securityVersion = null)
    {
        if (user is null)
            return;

        _sessions.LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            new UserAuthority(user.Id, securityVersion ?? user.SecurityVersion, true, user.Email is not null, user.IsEmailVerified));
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsNotFoundError()
    {
        var command = new ChangeMyPasswordCommand(Guid.NewGuid(), "OldPass123!", "NewPass123!");
        _sessions.LockUserAuthorityAsync(command.UserId, Arg.Any<CancellationToken>()).Returns((UserAuthority?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserNotFound.Code);
    }

    [Fact]
    public async Task HandleAsync_PasswordNotSet_ReturnsError()
    {
        var user = User.CreateWithPhone("+911234567890", "First", "Last");
        AccountIs(user);
        var command = new ChangeMyPasswordCommand(user.Id, "OldPass123!", "NewPass123!");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PasswordNotSet.Code);
    }

    [Fact]
    public async Task HandleAsync_WrongCurrentPassword_ReturnsErrorAndEndsNothing()
    {
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        AccountIs(user);
        var command = new ChangeMyPasswordCommand(user.Id, "WrongPass!", "NewPass123!");
        _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash!).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidCurrentPassword.Code);
        await _sessions.DidNotReceive().RevokeAllAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The password change and the end of every device commit together, so there is no moment in
    /// which the new password is live and an old device still is.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ValidChange_WritesThePasswordAndEndsEveryDeviceInOneTransaction()
    {
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        AccountIs(user);
        var command = new ChangeMyPasswordCommand(user.Id, "OldPass123!", "NewPass123!");
        _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash!).Returns(true);
        _passwordHasher.Hash(command.NewPassword).Returns("new-hash");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("new-hash", user.PasswordHash);
        await _sessions.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        await _sessions.Received(1).RevokeAllAsync(
            user.Id, null, "password-changed", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _sessions.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A user instance loaded before the lock could carry a stale password hash. The version read
    /// under the lock is the authority, so a mismatch stops the change rather than verifying a
    /// credential that has already been replaced.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheLockedAccountMovedOn_RefusesTheChange()
    {
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        AccountIs(user, securityVersion: user.SecurityVersion + 1);
        var command = new ChangeMyPasswordCommand(user.Id, "OldPass123!", "NewPass123!");
        _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash!).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.SessionInvalidated.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTransactionCannotCommit_ReportsFailure()
    {
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        AccountIs(user);
        _sessions.CommitAsync(Arg.Any<CancellationToken>()).Returns(false);
        var command = new ChangeMyPasswordCommand(user.Id, "OldPass123!", "NewPass123!");
        _passwordHasher.Verify(command.CurrentPassword, user.PasswordHash!).Returns(true);
        _passwordHasher.Hash(command.NewPassword).Returns("new-hash");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.SessionInvalidated.Code);
    }
}
