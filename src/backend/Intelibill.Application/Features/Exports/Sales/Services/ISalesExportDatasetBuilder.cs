using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public interface ISalesExportDatasetBuilder
{
    Task<SalesExportDatasetDto> BuildAsync(
        Shop shop,
        User generatedBy,
        DateOnly startDate,
        DateOnly endDate,
        string level,
        CancellationToken cancellationToken);
}
