using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>
/// The one balance projection for invoices, purchase receipts and unallocated payment credit.
/// The account UI, the allocation rules and the return due cap all read it, so they agree.
/// </summary>
/// <remarks>
/// Customer invoice outstanding = original due − active returns' due reduction − active
/// allocations, never below zero. Supplier receipt outstanding = effective payable of a resolved,
/// unvoided receipt − active allocations. An allocation is active while its receipt is unreversed.
/// </remarks>
public interface IInvoiceBalanceReader
{
    /// <param name="saleIds">Only these sales; null for every credit sale of the customer.</param>
    Task<IReadOnlyList<CustomerInvoiceBalanceDto>> GetForCustomerAsync(
        Guid shopId,
        Guid customerId,
        IReadOnlyCollection<Guid>? saleIds,
        CancellationToken cancellationToken);

    /// <param name="receiptValueIds">Only these receipts; null for every payable receipt of the supplier.</param>
    Task<IReadOnlyList<SupplierReceiptBalanceDto>> GetForSupplierAsync(
        Guid shopId,
        Guid supplierId,
        IReadOnlyCollection<Guid>? receiptValueIds,
        CancellationToken cancellationToken);

    /// <summary>Every receipt of the party with its allocated and unallocated amount, newest first.</summary>
    Task<IReadOnlyList<PaymentReceiptSummaryDto>> GetReceiptsAsync(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        CancellationToken cancellationToken);
}
