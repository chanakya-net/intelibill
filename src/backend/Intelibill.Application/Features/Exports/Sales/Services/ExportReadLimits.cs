namespace Intelibill.Application.Features.Exports.Sales.Services;

public static class ExportReadLimits
{
    public const int PageSize = 1_000;
    public const long PdfRows = 2_000;
    public const long FileRows = 50_000;
    public const long SourceRows = 50_000;
}
