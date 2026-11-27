using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Common;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Errors = Intelibill.Application.Common.Errors.Errors;

namespace Intelibill.Infrastructure.Services.Invoicing;

/// <summary>
/// Opens the series row on first use and locks it (<c>FOR UPDATE</c>) for the rest of the caller's
/// transaction, so concurrent postings and offline reservations of one series run one at a time.
/// </summary>
/// <remarks>
/// The insert does nothing on any unique conflict: the row may already be this shop's, or the
/// series may be registered to another shop under the same GSTIN. Row-level security hides other
/// shops' rows, so the second case shows up as no row of this shop's to lock.
/// </remarks>
internal sealed class InvoiceNumberAllocator(ApplicationDbContext context) : IInvoiceNumberAllocator
{
    public async Task<ErrorOr<string>> AllocateAsync(InvoiceSeriesKey series, CancellationToken cancellationToken)
    {
        var range = await ReserveRangeAsync(series, 1, cancellationToken);
        if (range.IsError)
            return range.Errors;

        return InvoiceNumberFormat.Format(range.Value.Prefix, range.Value.Start);
    }

    public async Task<ErrorOr<ReservedInvoiceRange>> ReserveRangeAsync(InvoiceSeriesKey series, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Invoice numbers are allocated inside the posting transaction.");

        var prefix = InvoiceNumberFormat.Prefix(series.SeriesCode, series.FiscalYear);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO invoice_sequences (id, shop_id, fiscal_year_start, series_code, seller_registration_key, next_number, prefix, created_at)
            VALUES ({0}, {1}, {2}, {3}, {4}, 1, {5}, now())
            ON CONFLICT DO NOTHING;
            """,
            [Guid.NewGuid(), series.ShopId, series.FiscalYear.StartYear, series.SeriesCode, series.SellerRegistrationKey, prefix],
            cancellationToken);

        var sequence = await context.InvoiceSequences
            .FromSqlRaw(
                "SELECT * FROM invoice_sequences WHERE shop_id = {0} AND fiscal_year_start = {1} AND series_code = {2} FOR UPDATE",
                series.ShopId,
                series.FiscalYear.StartYear,
                series.SeriesCode)
            .SingleOrDefaultAsync(cancellationToken);

        if (sequence is null)
            return Errors.Invoice.SeriesRegisteredToAnotherShop;
        if (!string.Equals(sequence.SellerRegistrationKey, series.SellerRegistrationKey, StringComparison.Ordinal))
            return Errors.Invoice.SeriesRegistrationChanged;
        if ((long)sequence.NextNumber + count - 1 > InvoiceNumberFormat.Capacity(sequence.Prefix))
            return Errors.Invoice.SeriesExhausted;

        var (start, end) = sequence.ReserveBlock(count);
        return new ReservedInvoiceRange(sequence.Id, sequence.Prefix, start, end);
    }
}
