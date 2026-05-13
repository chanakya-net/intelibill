namespace Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

public sealed record ExportSalesQuery(
    Guid UserId,
    Guid ShopId,
    string Format,
    string Level,
    DateOnly StartDate,
    DateOnly EndDate);
