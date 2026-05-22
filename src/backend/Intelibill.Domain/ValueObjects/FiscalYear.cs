using Intelibill.Domain.Common;

namespace Intelibill.Domain.ValueObjects;

public readonly record struct FiscalYear(int StartYear)
{
    public int EndYear => StartYear + 1;

    public string Label => $"{StartYear}-{EndYear % 100:00}";

    public string InvoicePrefix => $"INV-{Label}-";

    public static FiscalYear ForDate(DateTimeOffset timestamp, int startMonth = InvoiceLeaseDefaults.FiscalYearStartMonth)
    {
        var utc = timestamp.UtcDateTime;
        var startYear = utc.Month >= startMonth ? utc.Year : utc.Year - 1;
        return new FiscalYear(startYear);
    }
}
