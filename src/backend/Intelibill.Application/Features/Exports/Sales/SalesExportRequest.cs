namespace Intelibill.Application.Features.Exports.Sales;

public sealed record SalesExportRequest(
    string Format,
    string Level,
    DateOnly StartDate,
    DateOnly EndDate);
