using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RequestPasswordReset;

public class RequestPasswordResetCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IEmailOwnershipRepository _ownership = Substitute.For<IEmailOwnershipRepository>();
    private readonly RequestPasswordResetCommandHandler _handler;

    public RequestPasswordResetCommandHandlerTests()
    {
        _ownership.BeginAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _ownership.CommitAsync(Arg.Any<CancellationToken>()).Returns(true);
        _ownership.LockCurrentCredentialAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _emailService.IsAvailable.Returns(true);
        _handler = new RequestPasswordResetCommandHandler(_userRepository, _passwordResetTokenRepository, _emailService, _ownership, NullLogger<RequestPasswordResetCommandHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_DeliveryDisabled_DoesNotLookUpAccount()
    {
        _emailService.IsAvailable.Returns(false);
        var result = await _handler.HandleAsync(new RequestPasswordResetCommand("known@test.com", "https://app.test"), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Auth.EmailDeliveryUnavailable", result.FirstError.Code);
        await _userRepository.DidNotReceiveWithAnyArgs().GetByEmailAsync(default!, default);
        await _passwordResetTokenRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsSuccessWithoutSendingEmail()
    {
        var command = new RequestPasswordResetCommand("notfound@test.com", "https://app.test");
        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _passwordResetTokenRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _emailService.DidNotReceiveWithAnyArgs().SendPasswordResetAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_UserFound_GeneratesTokenAndSendsEmail()
    {
        var command = new RequestPasswordResetCommand("found@test.com", "https://app.test");
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hash", "first", "last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _passwordResetTokenRepository.Received(1).AddAsync(Arg.Is<PasswordResetToken>(t => t.UserId == user.Id), Arg.Any<CancellationToken>());
        await _ownership.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _emailService.Received(1).SendPasswordResetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(dt => dt > DateTimeOffset.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UserFound_PasswordResetTokenExpires15Minutes()
    {
        var before = DateTimeOffset.UtcNow;
        var command = new RequestPasswordResetCommand("found@test.com", "https://app.test");
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hash", "first", "last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _emailService.Received(1).SendPasswordResetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(dt => dt >= before.AddMinutes(15).AddSeconds(-2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UserFound_InvalidatesPreviousTokens()
    {
        var command = new RequestPasswordResetCommand("found@test.com", "https://app.test");
        var user = VerifiedUsers.CreateWithEmail(command.Email, "hash", "first", "last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _passwordResetTokenRepository.Received(1).InvalidateAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
    }
}
