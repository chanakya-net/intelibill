using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class PaymentReceiptTests
{
    private static PaymentReceipt Receipt(PaymentPartyType party = PaymentPartyType.Customer, decimal amount = 100m) =>
        PaymentReceipt.Record(
            Guid.NewGuid(), party, Guid.NewGuid(), amount, ReceiptTenderMethod.Upi, null, "  UPI-1  ",
            new DateOnly(2026, 11, 6), DateTimeOffset.UtcNow, "  ", Guid.NewGuid(), null, Guid.NewGuid()).Value;

    [Fact]
    public void Record_TrimsReferenceAndBlankNotes()
    {
        var receipt = Receipt();
        Assert.Equal("UPI-1", receipt.ExternalReference);
        Assert.Null(receipt.Notes);
        Assert.False(receipt.IsLegacy);
        Assert.Equal(PaymentAllocationTargetType.CustomerInvoice, receipt.AllocationTargetType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public void Record_RefusesNonPositiveOrSubPaiseAmount(double amount)
    {
        var result = PaymentReceipt.Record(
            Guid.NewGuid(), PaymentPartyType.Customer, Guid.NewGuid(), (decimal)amount, ReceiptTenderMethod.Cash, null, null,
            new DateOnly(2026, 11, 6), DateTimeOffset.UtcNow, null, Guid.NewGuid(), null, Guid.NewGuid());
        Assert.Equal("Payment.AmountInvalid", result.FirstError.Code);
    }

    [Fact]
    public void Record_RefusesExpenseOnCustomerReceipt()
    {
        var result = PaymentReceipt.Record(
            Guid.NewGuid(), PaymentPartyType.Customer, Guid.NewGuid(), 10m, ReceiptTenderMethod.Cash, null, null,
            new DateOnly(2026, 11, 6), DateTimeOffset.UtcNow, null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal("Payment.ExpenseNotAllowed", result.FirstError.Code);
    }

    [Fact]
    public void Allocation_TakesTargetTypeFromReceiptParty()
    {
        var receipt = Receipt(PaymentPartyType.Supplier);
        var allocation = PaymentAllocation.Create(receipt, Guid.NewGuid(), 40m, Guid.NewGuid()).Value;
        Assert.Equal(PaymentAllocationTargetType.SupplierReceipt, allocation.TargetType);
        Assert.Equal(receipt.Id, allocation.ReceiptId);
        Assert.Equal(receipt.ShopId, allocation.ShopId);
    }

    [Fact]
    public void Reversal_CarriesWholeAmountAndRequiresReason()
    {
        var receipt = Receipt(amount: 250m);
        Assert.Equal("Payment.ReversalReasonInvalid", PaymentReversal.Create(receipt, " x ", Guid.NewGuid(), new DateOnly(2026, 11, 6), Guid.NewGuid()).FirstError.Code);

        var reversal = PaymentReversal.Create(receipt, "  Bounced cheque ", Guid.NewGuid(), new DateOnly(2026, 11, 6), Guid.NewGuid()).Value;
        Assert.Equal(250m, reversal.Amount);
        Assert.Equal("Bounced cheque", reversal.Reason);
    }

    [Fact]
    public void PaymentReversalLedgerType_RestoresTheBalance()
    {
        Assert.Equal(75m, CustomerLedgerEntryType.PaymentReversal.ToBalanceDelta(75m));
        Assert.Equal("CustomerLedgerEntry.SaleIdNotAllowed",
            CustomerLedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CustomerLedgerEntryType.PaymentReversal, 1m, new DateOnly(2026, 11, 6), null, Guid.NewGuid()).FirstError.Code);
    }
}
