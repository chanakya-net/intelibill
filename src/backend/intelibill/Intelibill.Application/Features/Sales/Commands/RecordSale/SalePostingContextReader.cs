using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Captures the seller profile, sale instant, business day and invoice series a sale is posted under.</summary>
public sealed class SalePostingContextReader(
    IShopRepository shopRepository,
    IBusinessClock businessClock)
{
    internal sealed record SalePostingContext(
        InvoiceSellerProfile SellerProfile,
        DateTimeOffset SoldAt,
        BusinessDay BusinessDay,
        FiscalYear FiscalYear,
        InvoiceSeriesKey Series);

    internal async Task<ErrorOr<SalePostingContext>> CapturePostingContextAsync(
        RecordSaleCommand command,
        CancellationToken cancellationToken)
    {
        // Refused before a number is taken: an undeclared or unsupported profile issues nothing.
        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var sellerProfileOrError = InvoiceDocumentBuilder.CaptureSellerProfile(shop);
        if (sellerProfileOrError.IsError)
            return sellerProfileOrError.Errors;

        var sellerProfile = sellerProfileOrError.Value;
        var soldAt = businessClock.UtcNow;
        var businessDay = businessClock.GetBusinessDay(soldAt, shop.BusinessTimeZoneId);
        var fiscalYear = FiscalYear.ForBusinessDate(businessDay.Date);
        var series = new InvoiceSeriesKey(shop.Id, shop.SellerRegistrationKey, sellerProfile.SeriesCode, fiscalYear);
        return new SalePostingContext(sellerProfile, soldAt, businessDay, fiscalYear, series);
    }
}
