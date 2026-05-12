using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Application.Common.Behaviours;
using NSubstitute;
using Wolverine;

namespace Intelibill.Application.Unit.Tests.Behaviours;

public class ValidationMiddlewareTests
{
    public record FakeCommand;
    public record FakeResponse;

    [Fact]
    public async Task BeforeAsync_WhenNoValidatorRegistered_ReturnsContinueWithDefault()
    {
        // Arrange
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IValidator<FakeCommand>)).Returns(null);

        var middleware = new ValidationMiddleware(serviceProvider);

        // Act
        var (continuation, errorOr) = await middleware.BeforeAsync<FakeCommand, FakeResponse>(new FakeCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(HandlerContinuation.Continue, continuation);
        Assert.Null(errorOr);
    }

    [Fact]
    public async Task BeforeAsync_WhenValidationPasses_ReturnsContinueWithDefault()
    {
        // Arrange
        var validator = Substitute.For<IValidator<FakeCommand>>();
        var validationResult = new ValidationResult();
        validator.ValidateAsync(Arg.Any<FakeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validationResult));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IValidator<FakeCommand>)).Returns(validator);

        var middleware = new ValidationMiddleware(serviceProvider);

        // Act
        var (continuation, errorOr) = await middleware.BeforeAsync<FakeCommand, FakeResponse>(new FakeCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(HandlerContinuation.Continue, continuation);
        Assert.Null(errorOr);
    }

    [Fact]
    public async Task BeforeAsync_WhenValidationFails_ReturnsStopWithValidationErrors()
    {
        // Arrange
        var failures = new[]
        {
            new ValidationFailure("Email", "Email is required"),
            new ValidationFailure("Name", "Name is required")
        };
        var validationResult = new ValidationResult(failures);

        var validator = Substitute.For<IValidator<FakeCommand>>();
        validator.ValidateAsync(Arg.Any<FakeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validationResult));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IValidator<FakeCommand>)).Returns(validator);

        var middleware = new ValidationMiddleware(serviceProvider);

        // Act
        var (continuation, errorOr) = await middleware.BeforeAsync<FakeCommand, FakeResponse>(new FakeCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(HandlerContinuation.Stop, continuation);
        Assert.NotNull(errorOr);
        var errorOrValue = errorOr.Value;
        Assert.True(errorOrValue.IsError);
        Assert.Equal(2, errorOrValue.Errors.Count);
        Assert.All(errorOrValue.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));
    }
}
