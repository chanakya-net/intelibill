using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Wolverine.Attributes;

namespace Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

[WolverineHandler]
public sealed class ExportSalesQueryHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IShopRepository _shopRepository;
    private readonly ISalesExportDatasetBuilder _datasetBuilder;
    private readonly ISalesExcelExportRenderer _excelRenderer;
    private readonly ISalesPdfExportRenderer _pdfRenderer;
    private readonly ISalesTallyXmlExportRenderer _tallyRenderer;

    public ExportSalesQueryHandler(
        IUserRepository userRepository,
        IShopRepository shopRepository,
        ISalesExportDatasetBuilder datasetBuilder,
        ISalesExcelExportRenderer excelRenderer,
        ISalesPdfExportRenderer pdfRenderer,
        ISalesTallyXmlExportRenderer tallyRenderer)
    {
        _userRepository = userRepository;
        _shopRepository = shopRepository;
        _datasetBuilder = datasetBuilder;
        _excelRenderer = excelRenderer;
        _pdfRenderer = pdfRenderer;
        _tallyRenderer = tallyRenderer;
    }

    public async Task<ErrorOr<SalesExportResult>> Handle(
        ExportSalesQuery query,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
        {
            return Errors.General.NotFound(nameof(user), query.UserId);
        }

        var shop = await _shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
        {
            return Errors.Shop.ShopNotFound;
        }

        var membership = await _shopRepository.GetMembershipAsync(
            query.UserId,
            query.ShopId,
            cancellationToken);
        if (membership is null)
        {
            return Errors.Shop.MembershipNotFound;
        }

        if (membership.Role == ShopRole.Staff)
        {
            return Errors.Export.UserIsNotOwnerOrManager;
        }

        var dataset = await _datasetBuilder.BuildAsync(
            shop,
            user,
            query.StartDate,
            query.EndDate,
            query.Level,
            cancellationToken);

        return query.Format switch
        {
            _ when string.Equals(query.Format, SalesExportFormat.Xlsx, StringComparison.OrdinalIgnoreCase) =>
                await _excelRenderer.RenderAsync(dataset, cancellationToken),
            _ when string.Equals(query.Format, SalesExportFormat.Pdf, StringComparison.OrdinalIgnoreCase) =>
                await _pdfRenderer.RenderAsync(dataset, cancellationToken),
            _ when string.Equals(query.Format, SalesExportFormat.TallyXml, StringComparison.OrdinalIgnoreCase) =>
                await _tallyRenderer.RenderAsync(dataset, cancellationToken),
            _ => Error.Validation("Export.UnsupportedFormat", "The requested export format is not supported.")
        };
    }
}
