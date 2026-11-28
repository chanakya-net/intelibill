using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.SupplierReturns.Services;

public static class SupplierReturnPolicy
{
    public static decimal ReturnableQuantity(decimal available, decimal received, decimal shipped) =>
        Math.Max(0, Math.Min(available, received - shipped));

    internal static Error? CheckCompletion(ShopLicenceLease lease, IReadOnlyList<PurchaseReceiptValue> receipts,
        Guid supplierId, IBusinessClock clock, DateTimeOffset receivedBy, SupplierReturn? shipment = null)
    {
        var decision = lease.CurrentDecision();
        if (decision.EnforcedDenial() is { } denied)
            return denied;
        if (decision is not { Enforced: true, Outcome: LicenceOutcome.RequiresProvenSource })
            return null;

        var now = clock.UtcNow;
        var latestReceipt = receivedBy <= now ? receivedBy : now;
        if (receipts.Count == 0 || receipts.Any(receipt =>
            receipt.ShopId != lease.ShopId || receipt.SupplierId != supplierId ||
            receipt.ReceivedAt == default || receipt.ReceivedAt > latestReceipt))
            return SourceIneligible;

        if (shipment is not null && (shipment.ShopId != lease.ShopId || shipment.SupplierId != supplierId ||
            shipment.ShippedAt > now ||
            (shipment.Credit is { } credit && shipment.ShippedAt > credit.AcceptedAt) ||
            shipment.BusinessDate != clock.GetBusinessDate(shipment.ShippedAt, shipment.BusinessTimeZoneId)))
            return SourceIneligible;

        return null;
    }

    private static Error SourceIneligible => Errors.SupplierReturn.SourceIneligible;
}
