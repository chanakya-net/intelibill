using System.Globalization;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// An Indian fiscal year (1 April to 31 March), read on the business calendar: a sale at 00:30 IST
/// on 1 April belongs to the new year even though it is still 31 March in UTC.
/// </summary>
public readonly record struct FiscalYear(int StartYear)
{
    public int EndYear => StartYear + 1;

    public string Label => $"{StartYear}-{EndYear % 100:00}";

    /// <summary>The four-digit form used inside invoice numbers: 2026-27 is <c>2627</c>.</summary>
    public string ShortLabel => string.Create(CultureInfo.InvariantCulture, $"{StartYear % 100:00}{EndYear % 100:00}");

    /// <summary>The prefix of offline ranges reserved before fiscal series existed; kept only for those leases.</summary>
    public string InvoicePrefix => $"INV-{Label}-";

    public DateTimeOffset StartsAt => BusinessCalendar.StartOf(new DateOnly(StartYear, InvoiceLeaseDefaults.FiscalYearStartMonth, 1));

    public DateTimeOffset NextStartsAt => BusinessCalendar.StartOf(new DateOnly(EndYear, InvoiceLeaseDefaults.FiscalYearStartMonth, 1));

    public static FiscalYear ForDate(DateTimeOffset timestamp) => ForBusinessDate(BusinessCalendar.DateOf(timestamp));

    public static FiscalYear ForBusinessDate(DateOnly date) =>
        new(date.Month >= InvoiceLeaseDefaults.FiscalYearStartMonth ? date.Year : date.Year - 1);
}
