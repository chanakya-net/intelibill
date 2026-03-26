using FluentValidation;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RequestPasswordReset;

public class RequestPasswordResetCommandHandlerTests
{
    private readonly IValidator<RequestPasswordResetCommand> _validator = Substitute.For<IValidator<RequestPasswordResetCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RequestPasswordResetCommandHandler _handler;

    public RequestPasswordResetCommandHandlerTests()
    {
        _handler = new RequestPasswordResetCommandHandler(_validator, _userRepository, _passwordResetTokenRepository, _emailService, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<RequestPasswordResetCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
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
        var user = User.CreateWithEmail(command.Email, "hash", "first", "last");

        _userRepository.GetByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _passwordResetTokenRepository.Received(1).AddAsync(Arg.Is<PasswordResetToken>(t => t.UserId == user.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _emailService.Received(1).SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
