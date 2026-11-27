namespace Intelibill.Application.Features.Exports.ProfitLoss;

public sealed record ProfitLossExportResult(
    byte[] Content,
    string ContentType,
    string FileName);
