using Intelibill.Application.Features.Exports.Accounting;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Accounting;

public class AccountingVoucherValidatorTests
{
    private readonly AccountingVoucherValidator _validator = new();

    [Fact]
    public void Balanced_Passes()
    {
        var result = _validator.Validate(Voucher(
            new AccountingVoucherLine("Sales", 42.37m, IsDebit: true),
            new AccountingVoucherLine("Output GST 18%", 7.63m, IsDebit: true),
            new AccountingVoucherLine("Customer Refunds Clearing", 50m, IsDebit: false)));

        Assert.False(result.IsError);
    }

    [Fact]
    public void Unbalanced_ReportsBothSums()
    {
        var result = _validator.Validate(Voucher(
            new AccountingVoucherLine("Sales", 100m, IsDebit: true),
            new AccountingVoucherLine("Output GST 18%", 18m, IsDebit: true),
            new AccountingVoucherLine("Walk-in Customer", 50m, IsDebit: false)));

        Assert.True(result.IsError);
        Assert.Equal("Export.VoucherUnbalanced", result.FirstError.Code);
        Assert.Contains("RET-1", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Contains("debits 118.00", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Contains("credits 50.00", result.FirstError.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void OnePaisaShort_IsUnbalanced()
    {
        var result = _validator.Validate(Voucher(
            new AccountingVoucherLine("Sales", 33.33m, IsDebit: true),
            new AccountingVoucherLine("Customer Refunds Clearing", 33.34m, IsDebit: false)));

        Assert.Equal("Export.VoucherUnbalanced", result.FirstError.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.005)]
    public void ZeroNegativeOrFractionalPaiseLine_Refused(double amount)
    {
        var result = _validator.Validate(Voucher(
            new AccountingVoucherLine("Sales", (decimal)amount, IsDebit: true),
            new AccountingVoucherLine("Walk-in Customer", (decimal)amount, IsDebit: false)));

        Assert.Equal("Export.VoucherInvalid", result.FirstError.Code);
    }

    [Fact]
    public void SingleLine_Refused()
    {
        var result = _validator.Validate(Voucher(new AccountingVoucherLine("Sales", 10m, IsDebit: true)));

        Assert.Equal("Export.VoucherInvalid", result.FirstError.Code);
    }

    [Fact]
    public void UnnamedLedger_Refused()
    {
        var result = _validator.Validate(Voucher(
            new AccountingVoucherLine(" ", 10m, IsDebit: true),
            new AccountingVoucherLine("Sales", 10m, IsDebit: false)));

        Assert.Equal("Export.VoucherInvalid", result.FirstError.Code);
    }

    private static AccountingVoucher Voucher(params AccountingVoucherLine[] lines) =>
        new("RET-1", "Credit Note", new DateOnly(2026, 6, 12), "INV-1", new DateOnly(2026, 6, 10), null, lines);
}
