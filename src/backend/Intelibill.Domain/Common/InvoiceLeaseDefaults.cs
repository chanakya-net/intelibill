namespace Intelibill.Domain.Common;

public static class InvoiceLeaseDefaults
{
    public const int DefaultBlockSize = 200;
    public const int MaxBlockSize = 1000;
    public const int RenewalThreshold = 25;
    public const int LeaseDurationDays = 7;
    public const int NumberPadding = 6;
    public const int FiscalYearStartMonth = 4;
}
