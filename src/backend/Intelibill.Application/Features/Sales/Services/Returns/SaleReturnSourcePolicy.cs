using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services.Returns;

internal static class SaleReturnSourcePolicy
{
    internal static Error? Denial(LicenceDecision decision) =>
        decision.Enforced && decision.Outcome == LicenceOutcome.Blocked
            ? decision.Denial ?? Errors.Licence.SecuritySuspended : null;

    internal static Error? Check(ShopLicenceLease lease, Sale sale, IBusinessClock clock, SaleReturn? reversal = null)
    {
        var decision = lease.CurrentDecision();
        if (Denial(decision) is { } denial) return denial;
        if (!decision.Enforced || decision.Outcome != LicenceOutcome.RequiresProvenSource) return null;

        var now = clock.UtcNow;
        if (sale.ShopId != lease.ShopId || sale.Status != SaleStatus.Posted
            || sale.SoldAt == default || sale.SoldAt > now || sale.BusinessDate == default
            || sale.BusinessDate > clock.GetBusinessDate(now, sale.BusinessTimeZoneId)
            || string.IsNullOrWhiteSpace(sale.InvoiceNumber)
            || (decision.SourceCompletionCutoff is { } cutoff && sale.SoldAt >= cutoff))
            return Errors.Sale.ReturnSourceIneligible;

        return reversal is null || IsLinkedReturn(reversal, sale, now)
            ? null : Errors.Sale.ReturnSourceIneligible;
    }

    private static bool IsLinkedReturn(SaleReturn priorReturn, Sale sale, DateTimeOffset now)
    {
        if (priorReturn.ShopId != sale.ShopId || priorReturn.SaleId != sale.Id
            || priorReturn.ProcessedAt < sale.SoldAt || priorReturn.ProcessedAt > now || priorReturn.Items.Count == 0)
            return false;

        var saleItems = sale.Items.ToDictionary(item => item.Id);
        return priorReturn.Items.All(item => item.ShopId == sale.ShopId
            && saleItems.TryGetValue(item.SaleItemId, out var original)
            && item.Quantity > 0m && item.Quantity <= original.Quantity);
    }
}
