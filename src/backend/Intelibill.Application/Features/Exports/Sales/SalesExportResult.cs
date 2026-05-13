namespace Intelibill.Application.Features.Exports.Sales;

public sealed record SalesExportResult(
    byte[] Content,
    string ContentType,
    string FileName);
