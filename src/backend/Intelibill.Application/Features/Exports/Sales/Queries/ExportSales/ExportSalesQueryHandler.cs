using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Wolverine.Attributes;

namespace Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

[WolverineHandler]
public sealed class ExportSalesQueryHandler
{
    private const int PdfMaxRows = 2000;

    private readonly IUserRepository _userRepository;
    private readonly IShopRepository _shopRepository;
    private readonly ISalesExportDatasetBuilder _datasetBuilder;
    private readonly ISalesExcelExportRenderer _excelRenderer;
    private readonly ISalesPdfExportRenderer _pdfRenderer;
    private readonly ISalesTallyXmlExportRenderer _tallyRenderer;
    private readonly IExportFileNameBuilder _fileNameBuilder;
    private readonly IBusinessClock _businessClock;

    public ExportSalesQueryHandler(
        IUserRepository userRepository,
        IShopRepository shopRepository,
        ISalesExportDatasetBuilder datasetBuilder,
        ISalesExcelExportRenderer excelRenderer,
        ISalesPdfExportRenderer pdfRenderer,
        ISalesTallyXmlExportRenderer tallyRenderer,
        IExportFileNameBuilder fileNameBuilder,
        IBusinessClock businessClock)
    {
        _userRepository = userRepository;
        _shopRepository = shopRepository;
        _datasetBuilder = datasetBuilder;
        _excelRenderer = excelRenderer;
        _pdfRenderer = pdfRenderer;
        _tallyRenderer = tallyRenderer;
        _fileNameBuilder = fileNameBuilder;
        _businessClock = businessClock;
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

        var datasetLevel = string.Equals(query.Format, SalesExportFormat.TallyXml, StringComparison.OrdinalIgnoreCase)
            ? SalesExportLevel.LineItems
            : query.Level;

        var endDate = query.EndDate ?? _businessClock.Today(shop.BusinessTimeZoneId);
        var startDate = query.StartDate ?? endDate.AddDays(-29);
        if (SalesExportRange.Validate(startDate, endDate) is { } rangeFailure)
            return Error.Validation("Export.InvalidDateRange", rangeFailure);

        var dataset = await _datasetBuilder.BuildAsync(
            shop,
            user,
            startDate,
            endDate,
            datasetLevel,
            cancellationToken);

        if (string.Equals(query.Format, SalesExportFormat.Pdf, StringComparison.OrdinalIgnoreCase))
        {
            var rowCount = string.Equals(datasetLevel, SalesExportLevel.LineItems, StringComparison.OrdinalIgnoreCase)
                ? dataset.LineItemRows.Count
                : dataset.SummaryRows.Count;

            if (rowCount > PdfMaxRows)
            {
                return Errors.Export.PdfRowLimitExceeded(PdfMaxRows);
            }
        }

        var rendererResult = query.Format switch
        {
            _ when string.Equals(query.Format, SalesExportFormat.Xlsx, StringComparison.OrdinalIgnoreCase) =>
                await _excelRenderer.RenderAsync(dataset, cancellationToken),
            _ when string.Equals(query.Format, SalesExportFormat.Pdf, StringComparison.OrdinalIgnoreCase) =>
                await _pdfRenderer.RenderAsync(dataset, cancellationToken),
            _ when string.Equals(query.Format, SalesExportFormat.TallyXml, StringComparison.OrdinalIgnoreCase) =>
                await _tallyRenderer.RenderAsync(dataset, cancellationToken),
            _ => (ErrorOr<SalesExportResult>)Error.Validation("Export.UnsupportedFormat", "The requested export format is not supported.")
        };

        if (rendererResult.IsError)
        {
            return rendererResult;
        }

        var fileName = _fileNameBuilder.BuildFileName(
            shop.Name,
            query.Format,
            query.Level,
            startDate,
            endDate);

        var contentType = _fileNameBuilder.GetContentType(query.Format);

        var renderedContent = rendererResult.Value;
        return new SalesExportResult(renderedContent.Content, contentType, fileName);
    }
}
