using FluentValidation.TestHelper;
using Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.RecordCustomerPayment;

public class RecordCustomerPaymentCommandValidatorTests
{
    private readonly RecordCustomerPaymentCommandValidator _validator = new();

    private static RecordCustomerPaymentCommand ValidCommand() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "Paid in cash");

    [Fact]
    public void Validate_WhenAmountIsNotPositive_ReturnsError()
    {
        var command = ValidCommand() with { Amount = 0m };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Amount)
            .WithErrorCode("Customer.PaymentAmountMustBePositive");
    }

    [Fact]
    public void Validate_WhenPaymentDateIsDefault_ReturnsError()
    {
        var command = ValidCommand() with { PaymentDate = default };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.PaymentDate)
            .WithErrorCode("Customer.PaymentDateRequired");
    }

    [Fact]
    public void Validate_WhenNotesTooLong_ReturnsError()
    {
        var command = ValidCommand() with { Notes = new string('a', 256) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Notes)
            .WithErrorCode("Customer.PaymentNotesTooLong");
    }
}
