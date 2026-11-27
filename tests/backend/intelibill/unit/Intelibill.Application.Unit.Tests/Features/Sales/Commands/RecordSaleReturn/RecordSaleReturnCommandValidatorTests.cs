using FluentValidation.TestHelper;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSaleReturn;

public sealed class RecordSaleReturnCommandValidatorTests
{
    private readonly RecordSaleReturnCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenCreditNoteReasonTooLong_ReturnsError()
    {
        var command = CreateCommand() with { CreditNoteReason = new string('x', 1001) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CreditNoteReason);
    }

    [Fact]
    public void Validate_WhenCreditNoteReasonWithinLimit_NoErrors()
    {
        var command = CreateCommand() with { CreditNoteReason = new string('x', 1000) };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.CreditNoteReason);
    }

    private static RecordSaleReturnCommand CreateCommand() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReturnPayoutDestination.CreditNote,
            null,
            null,
            null,
            null,
            null,
            [new RecordSaleReturnItemCommand(Guid.NewGuid(), 1m, SaleLineType.Goods, SaleReturnCondition.Restockable, null, null)]);
}
