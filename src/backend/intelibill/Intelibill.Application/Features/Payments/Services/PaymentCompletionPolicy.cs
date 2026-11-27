using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Services;

public sealed class PaymentCompletionPolicy(
    IInvoiceBalanceReader balances,
    ICustomerLedgerEntryRepository customers,
    ISupplierLedgerEntryRepository suppliers,
    IShopRepository shops,
    IBusinessClock clock)
{
    public async Task<ErrorOr<Success>> CheckSettlementAsync(ShopLicenceLease lease, PaymentReceipt payment,
        IReadOnlyList<PaymentAllocationRequest> allocations, CancellationToken ct)
    {
        var decision = lease.CurrentDecision();
        if (!RequiresProof(decision))
            return Allow(decision);

        var projected = await ReadBalancesAsync(payment.ShopId, payment.PartyType, payment.PartyId, ct);
        var capacity = await RemainingDueAsync(payment.ShopId, projected, payment.BusinessDate, ct);
        if (capacity.IsError) return capacity.Errors;

        decision = lease.CurrentDecision();
        if (Denial(decision) is { } denial) return denial;
        foreach (var allocation in allocations)
            if (!capacity.Value.EligibleDocuments.Contains(allocation.TargetId))
                return Errors.Payment.SettlementSourceIneligible;
        return payment.Amount <= capacity.Value.Amount ? Result.Success : Errors.Payment.SettlementLimitExceeded;
    }

    public async Task<ErrorOr<Success>> CheckReceiptAsync(ShopLicenceLease lease, PaymentReceipt receipt, CancellationToken ct)
    {
        var decision = lease.CurrentDecision();
        if (!RequiresProof(decision)) return Allow(decision);

        var shop = await shops.GetByIdAsync(receipt.ShopId, ct);
        if (shop is null) return Errors.Shop.ShopNotFound;
        if (Denial(lease.CurrentDecision()) is { } denial) return denial;
        return receipt.ReceivedAt <= clock.UtcNow && receipt.BusinessDate <= clock.Today(shop.BusinessTimeZoneId)
            && receipt.Amount > 0m && receipt.LedgerEntryId != Guid.Empty
            ? Result.Success : Errors.Payment.SettlementSourceIneligible;
    }

    internal async Task<PartyBalancesDto> ReadBalancesAsync(Guid shop, PaymentPartyType type, Guid party, CancellationToken ct)
    {
        var receipts = await balances.GetReceiptsAsync(shop, type, party, ct);
        var invoices = type == PaymentPartyType.Customer ? await balances.GetForCustomerAsync(shop, party, null, ct) : [];
        var purchases = type == PaymentPartyType.Supplier ? await balances.GetForSupplierAsync(shop, party, null, ct) : [];
        return new PartyBalancesDto(type, party, receipts.Sum(r => r.UnallocatedAmount), invoices, purchases, receipts);
    }

    internal async Task<ErrorOr<SettlementCapacity>> RemainingDueAsync(Guid shopId, PartyBalancesDto projected,
        DateOnly? paymentDate, CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(shopId, ct);
        if (shop is null) return Errors.Shop.ShopNotFound;
        var now = clock.UtcNow;
        var today = clock.GetBusinessDate(now, shop.BusinessTimeZoneId);
        var through = paymentDate is { } date && date < today ? date : today;
        bool IsEligible(DateTimeOffset recordedAt) =>
            recordedAt <= now && clock.GetBusinessDate(recordedAt, shop.BusinessTimeZoneId) <= through;

        var customer = projected.PartyType == PaymentPartyType.Customer;
        var signedBalance = customer
            ? await customers.GetCustomerBalanceAsync(shopId, projected.PartyId, ct)
            : await suppliers.GetSupplierBalanceAsync(shopId, projected.PartyId, ct);
        var invoices = projected.Invoices.Where(i => IsEligible(i.SoldAt)).ToList();
        var purchases = projected.PurchaseReceipts.Where(p => IsEligible(p.ReceivedAt)).ToList();

        // Allocations and document corrections already reduce the document balances. Deduct
        // credit that those balances have not absorbed, without deducting it again from the ledger.
        var documentDue = invoices.Sum(i => i.OutstandingAmount) + purchases.Sum(p => p.OutstandingAmount)
            - projected.UnallocatedCredit - projected.PurchaseReceipts.Sum(p => p.SupplierCreditAmount);
        var eligible = invoices.Where(i => i.OutstandingAmount > 0m).Select(i => i.SaleId)
            .Concat(purchases.Where(p => p.OutstandingAmount > 0m).Select(p => p.PurchaseReceiptValueId)).ToHashSet();
        return new SettlementCapacity(Math.Max(0m, Math.Min(signedBalance, documentDue)), eligible);
    }

    internal static bool RequiresProof(LicenceDecision decision) =>
        decision.Enforced && decision.Outcome == LicenceOutcome.RequiresProvenSource;

    internal static Error? Denial(LicenceDecision decision) =>
        decision.Enforced && decision.Outcome == LicenceOutcome.Blocked
            ? decision.Denial ?? Errors.Licence.SecuritySuspended : null;

    private static ErrorOr<Success> Allow(LicenceDecision decision) =>
        Denial(decision) is { } denial ? denial : Result.Success;

    internal sealed record SettlementCapacity(decimal Amount, HashSet<Guid> EligibleDocuments);
}
