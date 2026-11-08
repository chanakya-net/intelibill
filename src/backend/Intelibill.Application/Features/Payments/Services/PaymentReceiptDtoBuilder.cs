using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>Builds the full <see cref="PaymentReceiptDto"/> from committed (or just saved) rows.</summary>
public sealed class PaymentReceiptDtoBuilder(
    IPaymentReceiptRepository receiptRepository,
    IInvoiceBalanceReader balanceReader,
    ICustomerRepository customerRepository,
    ISupplierRepository supplierRepository,
    IUserRepository userRepository)
{
    public async Task<PaymentReceiptDto> BuildAsync(PaymentReceipt receipt, CancellationToken cancellationToken)
    {
        var ids = new[] { receipt.Id };
        var allocations = await receiptRepository.GetAllocationsAsync(receipt.ShopId, ids, cancellationToken);
        var reversal = (await receiptRepository.GetReversalsAsync(receipt.ShopId, ids, cancellationToken)).SingleOrDefault();

        var labels = await TargetLabelsAsync(receipt, allocations.Select(a => a.TargetId).Distinct().ToList(), cancellationToken);
        var partyName = receipt.PartyType == PaymentPartyType.Customer
            ? (await customerRepository.GetByShopAndIdAsync(receipt.ShopId, receipt.PartyId, cancellationToken))?.Name
            : (await supplierRepository.GetByIdAsync(receipt.PartyId, cancellationToken)) is { } supplier && supplier.ShopId == receipt.ShopId
                ? supplier.Name
                : null;

        var allocated = reversal is null ? allocations.Sum(a => a.Amount) : 0m;
        return new PaymentReceiptDto(
            receipt.Id,
            receipt.PartyType,
            receipt.PartyId,
            partyName,
            receipt.Amount,
            receipt.Method,
            receipt.BankAccountId,
            receipt.ExternalReference,
            receipt.BusinessDate,
            receipt.ReceivedAt,
            receipt.Notes,
            receipt.IsLegacy,
            receipt.CreatedBy,
            await UserNameAsync(receipt.CreatedBy, cancellationToken),
            receipt.LedgerEntryId,
            receipt.ExpenseId,
            allocated,
            reversal is null ? receipt.Amount - allocated : 0m,
            allocations
                .Select(a => new PaymentAllocationDto(
                    a.Id,
                    a.TargetType,
                    a.TargetId,
                    labels.GetValueOrDefault(a.TargetId),
                    a.Amount,
                    a.CreatedAt,
                    a.CreatedBy))
                .ToList(),
            reversal is null
                ? null
                : new PaymentReversalDto(
                    reversal.Id,
                    reversal.Amount,
                    reversal.Reason,
                    reversal.BusinessDate,
                    reversal.CreatedAt,
                    reversal.ReversedBy,
                    await UserNameAsync(reversal.ReversedBy, cancellationToken),
                    reversal.LedgerEntryId, reversal.SettlementEvidence));
    }

    private async Task<Dictionary<Guid, string>> TargetLabelsAsync(
        PaymentReceipt receipt,
        List<Guid> targetIds,
        CancellationToken cancellationToken)
    {
        if (targetIds.Count == 0)
            return [];

        if (receipt.PartyType == PaymentPartyType.Customer)
        {
            return (await balanceReader.GetForCustomerAsync(receipt.ShopId, receipt.PartyId, targetIds, cancellationToken))
                .ToDictionary(b => b.SaleId, b => b.InvoiceNumber);
        }

        return (await balanceReader.GetForSupplierAsync(receipt.ShopId, receipt.PartyId, targetIds, cancellationToken))
            .ToDictionary(b => b.PurchaseReceiptValueId, b => $"{b.ItemName} · {b.BatchNumber}");
    }

    private async Task<string?> UserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : $"{user.FirstName} {user.LastName}".Trim();
    }
}
