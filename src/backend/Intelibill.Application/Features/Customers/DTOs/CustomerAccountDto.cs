using Intelibill.Application.Features.Payments.DTOs;

namespace Intelibill.Application.Features.Customers.DTOs;

public sealed record CustomerAccountDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    decimal OutstandingDue,
    IReadOnlyList<CustomerAccountSaleDto> Sales,
    IReadOnlyList<CustomerLedgerEntryDto> LedgerEntries,
    IReadOnlyList<CustomerLedgerEntryDto> PaymentHistory,
    decimal UnallocatedCredit = 0m,
    IReadOnlyList<PaymentReceiptSummaryDto>? Receipts = null);
