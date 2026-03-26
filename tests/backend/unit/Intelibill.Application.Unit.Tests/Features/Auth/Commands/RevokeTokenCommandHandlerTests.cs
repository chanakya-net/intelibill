using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.Commands.RevokeToken;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RevokeToken;

public class RevokeTokenCommandHandlerTests
{
    private readonly IValidator<RevokeTokenCommand> _validator = Substitute.For<IValidator<RevokeTokenCommand>>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        _handler = new RevokeTokenCommandHandler(_validator, _refreshTokenRepository, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_InvalidToken_ReturnsError()
    {
        var command = new RevokeTokenCommand("invalid");
        _refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, Arg.Any<CancellationToken>())
                               .Returns((Domain.Entities.RefreshToken?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidRefreshToken.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_RevokesToken()
    {
        var command = new RevokeTokenCommand("valid");
        var token = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "valid", DateTimeOffset.UtcNow.AddDays(7));

        _refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, Arg.Any<CancellationToken>())
                               .Returns(token);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(token.IsRevoked);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
