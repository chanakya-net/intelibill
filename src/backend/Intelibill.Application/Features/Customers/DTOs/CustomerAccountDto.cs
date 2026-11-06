using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

/// <param name="OutstandingDue">The signed account balance today, across every entry (negative = in credit).</param>
/// <param name="UnallocatedCredit">Payment money received but not applied to any invoice.</param>
public sealed record CustomerAccountDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    decimal OutstandingDue,
    IReadOnlyList<CustomerAccountSaleDto> Sales,
    IReadOnlyList<CustomerLedgerEntryDto> LedgerEntries,
    IReadOnlyList<CustomerLedgerEntryDto> PaymentHistory,
    decimal UnallocatedCredit = 0m,
    IReadOnlyList<PaymentReceiptSummaryDto>? Receipts = null)
{
    /// <summary>The saved credit limit, exactly as configured.</summary>
    public decimal CreditLimit { get; init; }

    public bool IsActive { get; init; }

    public CreditPolicyMode CreditPolicyMode { get; init; }

    public int? CreditTermsDays { get; init; }

    /// <summary>New due the account can take now under an enforced policy; null when the limit is advisory.</summary>
    public decimal? AvailableCredit { get; init; }

    /// <summary>The balance before the first entry of the window (0 when the window starts at the beginning).</summary>
    public decimal OpeningBalance { get; init; }

    /// <summary>Opening balance plus the window's signed entries.</summary>
    public decimal ClosingBalance { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    /// <summary>The business date overdue is judged on.</summary>
    public DateOnly AsOfBusinessDate { get; init; }

    /// <summary>Outstanding on invoices whose due date is before <see cref="AsOfBusinessDate"/>.</summary>
    public decimal OverdueAmount { get; init; }
}
