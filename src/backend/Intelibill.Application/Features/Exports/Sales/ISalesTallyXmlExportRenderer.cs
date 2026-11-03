using ErrorOr;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Application.Features.Exports.Sales;

public interface ISalesTallyXmlExportRenderer
{
    /// <summary>
    /// The Tally import file, or one error per record that cannot be posted as a balanced voucher:
    /// a file is never written with such a record left out or balanced by an invented line.
    /// </summary>
    Task<ErrorOr<SalesExportResult>> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken);
}
