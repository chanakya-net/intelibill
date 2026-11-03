using ErrorOr;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Common.Interfaces;

/// <param name="ShopId">The shop that owns the series.</param>
/// <param name="SellerRegistrationKey">The registration the series is unique for (GSTIN, or the shop when unregistered).</param>
/// <param name="SeriesCode">The series code, e.g. <c>A</c>.</param>
/// <param name="FiscalYear">The fiscal year the numbers belong to.</param>
public sealed record InvoiceSeriesKey(Guid ShopId, string SellerRegistrationKey, string SeriesCode, FiscalYear FiscalYear);

public sealed record ReservedInvoiceRange(Guid SequenceId, string Prefix, int Start, int End);

/// <summary>
/// The one numbering authority for documents: online postings take one number, offline devices a
/// range, both from the same locked counter row. It works inside the caller's open transaction, so
/// the counter only advances when the posting commits and a failed posting leaves no gap.
/// </summary>
public interface IInvoiceNumberAllocator
{
    /// <summary>The next number of the series, formatted (e.g. <c>A/2627/000042</c>).</summary>
    Task<ErrorOr<string>> AllocateAsync(InvoiceSeriesKey series, CancellationToken cancellationToken);

    /// <summary>The next <paramref name="count"/> consecutive numbers of the series.</summary>
    Task<ErrorOr<ReservedInvoiceRange>> ReserveRangeAsync(InvoiceSeriesKey series, int count, CancellationToken cancellationToken);
}
