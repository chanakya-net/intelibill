using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public class FiscalYearTests
{
    [Fact]
    public void Rollover_follows_the_india_business_date()
    {
        // 1 April 2027 00:00 IST is 31 March 2027 18:30 UTC.
        Assert.Equal(2026, FiscalYear.ForDate(new DateTimeOffset(2027, 3, 31, 18, 29, 59, TimeSpan.Zero)).StartYear);
        Assert.Equal(2027, FiscalYear.ForDate(new DateTimeOffset(2027, 3, 31, 18, 30, 0, TimeSpan.Zero)).StartYear);
    }

    [Fact]
    public void Boundaries_are_midnight_ist_on_1_april()
    {
        var year = new FiscalYear(2026);

        Assert.Equal(new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero), year.StartsAt);
        Assert.Equal(new DateTimeOffset(2027, 3, 31, 18, 30, 0, TimeSpan.Zero), year.NextStartsAt);
        Assert.Equal("2026-27", year.Label);
        Assert.Equal("2627", year.ShortLabel);
    }

    [Fact]
    public void Business_date_reads_ist()
    {
        Assert.Equal(new DateOnly(2026, 11, 4), BusinessCalendar.DateOf(new DateTimeOffset(2026, 11, 3, 18, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 3), BusinessCalendar.DateOf(new DateTimeOffset(2026, 11, 3, 18, 29, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Invoice_numbers_fit_sixteen_characters()
    {
        var prefix = InvoiceNumberFormat.Prefix("A", new FiscalYear(2026));

        Assert.Equal("A/2627/", prefix);
        Assert.Equal("A/2627/000001", InvoiceNumberFormat.Format(prefix, 1));
        Assert.Equal(999_999_999, InvoiceNumberFormat.Capacity(prefix));

        var widest = InvoiceNumberFormat.Prefix("ABCD", new FiscalYear(2026));
        Assert.Equal(999_999, InvoiceNumberFormat.Capacity(widest));
        Assert.Equal(16, InvoiceNumberFormat.Format(widest, 999_999).Length);
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("b2", true)]
    [InlineData("ABCD", true)]
    [InlineData("ABCDE", false)]
    [InlineData("A/1", false)]
    [InlineData("", false)]
    public void Series_codes_are_one_to_four_letters_or_digits(string code, bool valid) =>
        Assert.Equal(valid, InvoiceNumberFormat.IsValidSeriesCode(code));
}
