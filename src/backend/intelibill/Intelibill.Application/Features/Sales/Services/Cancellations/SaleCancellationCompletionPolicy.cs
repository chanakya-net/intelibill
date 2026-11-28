using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Cancellations;

public sealed class SaleCancellationCompletionPolicy(
    SaleCancellationPolicy receipts, IShopLicenceFence fence, IBusinessClock clock)
{
    private static readonly SaleCancellationReasonDto InvalidSource = new(
        "SaleCancellation.PriorSourceIneligible", "The stored receipt must prove an earlier sale in this shop.");

    internal Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid shop, CancellationToken ct) =>
        fence.EnterAsync(shop, LicenceAction.SourceCorrection, ct);

    internal Task<SaleCancellationEvaluation> EvaluateReceiptAsync(Guid shop, Sale sale, BusinessDay day,
        CancellationToken ct) => receipts.EvaluateAsync(shop, sale, day, ct);

    internal static Error? Denial(LicenceDecision decision) =>
        decision.Enforced && decision.Outcome == LicenceOutcome.Blocked
            ? decision.Denial ?? Errors.Licence.SecuritySuspended : null;

    internal static bool RequiresProof(LicenceDecision decision) =>
        decision.Enforced && decision.Outcome == LicenceOutcome.RequiresProvenSource;

    internal SaleCancellationReasonDto? SourceRefusal(LicenceDecision decision, Shop shop, Sale sale)
    {
        if (!RequiresProof(decision)) return null;
        var now = clock.UtcNow;
        return sale.ShopId == shop.Id && sale.SoldAt <= now && sale.InvoiceDocument?.IssuedAt <= now &&
            sale.BusinessTimeZoneId == shop.BusinessTimeZoneId &&
            clock.GetBusinessDate(sale.SoldAt, shop.BusinessTimeZoneId) == sale.BusinessDate
            ? null : InvalidSource;
    }

    internal Error? PostingRefusal(ShopLicenceLease lease, Shop shop, Sale sale, BusinessDay day)
    {
        var decision = lease.CurrentDecision();
        if (Denial(decision) is { } denial) return denial;
        var currentDate = clock.Today(day.TimeZoneId);
        if (PostingPeriod.MonthOf(currentDate) != PostingPeriod.MonthOf(day.Date))
            return Errors.SaleCancellation.OtherMonthBlocked;
        if (currentDate != day.Date) return Errors.SaleCancellation.Busy;
        return SourceRefusal(decision, shop, sale) is { } invalid
            ? Error.Conflict(invalid.Code, invalid.Message) : null;
    }
}
