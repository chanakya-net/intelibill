using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RequestPasswordReset;

public class RequestPasswordResetCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IIdentityMailOutbox _mail = Substitute.For<IIdentityMailOutbox>();
    private readonly IEmailOwnershipRepository _ownership = Substitute.For<IEmailOwnershipRepository>();
    private readonly RequestPasswordResetCommandHandler _handler;

    public RequestPasswordResetCommandHandlerTests()
    {
        _ownership.BeginAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _ownership.CommitAsync(Arg.Any<CancellationToken>()).Returns(true);
        _ownership.LockCurrentCredentialAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.Enabled.Returns(true);
        _handler = new RequestPasswordResetCommandHandler(_userRepository, _passwordResetTokenRepository, _mail, _ownership);
    }

    [Fact]
    public async Task HandleAsync_DeliveryDisabled_DoesNotLookUpAccount()
    {
        _mail.Enabled.Returns(false);
        var result = await _handler.HandleAsync(new RequestPasswordResetCommand("known@test.com"), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Auth.EmailDeliveryUnavailable", result.FirstError.Code);
        await _userRepository.DidNotReceiveWithAnyArgs().GetByEmailAsync(default!, default);
        await _passwordResetTokenRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsSuccessWithoutQueueingMail()
    {
        var command = new RequestPasswordResetCommand("notfound@test.com");
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _passwordResetTokenRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _mail.DidNotReceiveWithAnyArgs().QueuePasswordReset(default, default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_UserFound_QueuesMailForTheSavedTokenBeforeCommit()
    {
        var before = DateTimeOffset.UtcNow;
        var command = new RequestPasswordResetCommand("found@test.com");
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hash", "first", "last");
        PasswordResetToken? saved = null;
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        await _passwordResetTokenRepository.AddAsync(Arg.Do<PasswordResetToken>(t => saved = t), Arg.Any<CancellationToken>());

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(saved);
        Assert.Equal(user.Id, saved.UserId);
        Received.InOrder(() =>
        {
            _passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
            _passwordResetTokenRepository.AddAsync(saved, Arg.Any<CancellationToken>());
            _mail.QueuePasswordReset(saved.Id, "found@test.com", Arg.Any<string>(), saved.ExpiresAt);
            _ownership.CommitAsync(Arg.Any<CancellationToken>());
        });
        Assert.True(saved.ExpiresAt >= before.AddMinutes(15).AddSeconds(-2));
    }

    [Fact]
    public async Task HandleAsync_UserFound_QueuesTheRawTokenAndStoresOnlyItsHash()
    {
        var command = new RequestPasswordResetCommand("found@test.com");
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hash", "first", "last");
        PasswordResetToken? saved = null;
        string? queued = null;
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);
        await _passwordResetTokenRepository.AddAsync(Arg.Do<PasswordResetToken>(t => saved = t), Arg.Any<CancellationToken>());
        _mail.QueuePasswordReset(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Do<string>(t => queued = t), Arg.Any<DateTimeOffset>());

        _ = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.NotNull(queued);
        Assert.NotEqual(queued, saved!.TokenHash);
        Assert.Equal(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(queued))), saved.TokenHash);
    }

    [Fact]
    public async Task HandleAsync_CommitFails_StillReturnsGenericSuccess()
    {
        var command = new RequestPasswordResetCommand("found@test.com");
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>())
            .Returns(VerifiedUsers.CreateWithEmail(command.Email, "hash", "first", "last"));
        _ownership.CommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Value);
    }
}
