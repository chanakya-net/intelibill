using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Application.Common.Extensions;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Common;

// Must be top-level (not nested) inside namespace so NSubstitute can proxy IValidator<DummyValidationCommand>.
public sealed class DummyValidationCommand { }

public class ValidationExtensionsTests
{
    [Fact]
    public async Task ValidateCommandAsync_WithNullValidator_ReturnsNull()
    {
        IValidator<DummyValidationCommand>? validator = null;
        var result = await validator.ValidateCommandAsync(new DummyValidationCommand(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCommandAsync_WithValidCommand_ReturnsNull()
    {
        var validator = Substitute.For<IValidator<DummyValidationCommand>>();
        validator.ValidateAsync(Arg.Any<DummyValidationCommand>(), Arg.Any<CancellationToken>())
                 .Returns(new ValidationResult()); // no failures

        var result = await validator.ValidateCommandAsync(new DummyValidationCommand(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCommandAsync_WithInvalidCommand_ReturnsMappedErrors()
    {
        var validator = Substitute.For<IValidator<DummyValidationCommand>>();
        var failures = new List<ValidationFailure>
        {
            new ValidationFailure("Prop1", "Error 1"),
            new ValidationFailure("Prop2", "Error 2")
        };
        validator.ValidateAsync(Arg.Any<DummyValidationCommand>(), Arg.Any<CancellationToken>())
                 .Returns(new ValidationResult(failures));

        var result = await validator.ValidateCommandAsync(new DummyValidationCommand(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Value.IsError);
        var errors = result.Value.Errors;
        Assert.Equal(2, errors.Count);
        Assert.Equal("Prop1", errors[0].Code);
        Assert.Equal("Error 1", errors[0].Description);
        Assert.Equal(ErrorType.Validation, errors[0].Type);
    }
}
