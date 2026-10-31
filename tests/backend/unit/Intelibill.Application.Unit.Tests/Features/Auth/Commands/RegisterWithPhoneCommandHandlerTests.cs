using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RegisterWithPhone;

public class RegisterWithPhoneCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IAuthSessionRepository _sessionRepository = Substitute.For<IAuthSessionRepository>();
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly RegisterWithPhoneCommandHandler _handler;

    public RegisterWithPhoneCommandHandlerTests()
    {
        _sessionRepository.BeginAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _sessionRepository.CommitAsync(Arg.Any<CancellationToken>()).Returns(true);
        _handler = new RegisterWithPhoneCommandHandler(_userRepository, _sessionRepository, _sessions, TimeProvider.System);
    }

    [Fact]
    public async Task HandleAsync_PhoneAlreadyInUse_ReturnsError()
    {
        var command = new RegisterWithPhoneCommand("+1234567890", "First", "Last");
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PhoneAlreadyInUse.Code);
        await _sessions.DidNotReceive().IssueForProvedAccountAsync(
            Arg.Any<User>(), Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The account and its first session are inserted by one transaction, so a registration either
    /// produces an account that can sign in or no account at all.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ValidCommand_RegistersUserAndItsFirstSessionTogether()
    {
        var command = new RegisterWithPhoneCommand("+1234567890", "First", "Last");
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(false);
        _sessions.IssueForProvedAccountAsync(
                Arg.Any<User>(), Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => AuthResults.For(call.Arg<User>()));

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        Assert.Equal("+1234567890", result.Value.User.PhoneNumber);

        await _userRepository.Received(1).AddAsync(Arg.Is<User>(u => u.PhoneNumber == command.PhoneNumber), Arg.Any<CancellationToken>());
        await _sessionRepository.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTheTransactionCannotCommit_ReturnsErrorAndNoResult()
    {
        var command = new RegisterWithPhoneCommand("+1234567890", "First", "Last");
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(false);
        _sessions.IssueForProvedAccountAsync(
                Arg.Any<User>(), Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => AuthResults.For(call.Arg<User>()));
        _sessionRepository.CommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PhoneAlreadyInUse.Code);
    }

    /// <summary>
    /// The registration proof names the account this transaction is inserting, so the authority the
    /// session is created under is stated at the call site rather than assumed by the service.
    /// </summary>
    [Fact]
    public async Task HandleAsync_NamesTheRegistrationProofForTheAccountItInserts()
    {
        var command = new RegisterWithPhoneCommand("+1234567890", "First", "Last");
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(false);
        _sessions.IssueForProvedAccountAsync(
                Arg.Any<User>(), Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => AuthResults.For(call.Arg<User>()));

        _ = await _handler.HandleAsync(command, CancellationToken.None);

        await _sessions.Received(1).IssueForProvedAccountAsync(
            Arg.Any<User>(),
            Arg.Is<AuthenticationProof>(p => p.Credential == CredentialKind.Registration),
            null,
            Arg.Any<CancellationToken>());
    }
}
