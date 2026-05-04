using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class CustomerLedgerEntryTests
{
    [Fact]
    public void Create_ReturnCredit_WithNullSaleId_Succeeds()
    {
        var result = CustomerLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            CustomerLedgerEntryType.ReturnCredit,
            50m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            " return credit ",
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Null(result.Value.SaleId);
        Assert.Equal("return credit", result.Value.Notes);
    }

    [Fact]
    public void Create_ReturnCreditReversal_WithSaleId_Succeeds()
    {
        var saleId = Guid.NewGuid();

        var result = CustomerLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            saleId,
            CustomerLedgerEntryType.ReturnCreditReversal,
            25m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal(saleId, result.Value.SaleId);
    }

    [Fact]
    public void Create_SaleDue_WithoutSaleId_ReturnsValidationError()
    {
        var result = CustomerLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            CustomerLedgerEntryType.SaleDue,
            25m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("CustomerLedgerEntry.SaleIdRequired", result.FirstError.Code);
    }

    [Fact]
    public void Create_PaymentReceived_WithSaleId_ReturnsValidationError()
    {
        var result = CustomerLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CustomerLedgerEntryType.PaymentReceived,
            25m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("CustomerLedgerEntry.SaleIdNotAllowed", result.FirstError.Code);
    }
}
