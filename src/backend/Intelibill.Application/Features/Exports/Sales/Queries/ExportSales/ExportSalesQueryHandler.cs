using Intelibill.Application.Features.Exports.Sales.DTOs;
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
    private readonly IExportReadStore _readStore;

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
        IBusinessClock businessClock,
        IExportReadStore readStore)
    {
        _userRepository = userRepository;
        _shopRepository = shopRepository;
        _datasetBuilder = datasetBuilder;
        _excelRenderer = excelRenderer;
        _pdfRenderer = pdfRenderer;
        _tallyRenderer = tallyRenderer;
        _fileNameBuilder = fileNameBuilder;
        _businessClock = businessClock;
        _readStore = readStore;
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

        if (!new[] { SalesExportFormat.Pdf, SalesExportFormat.Xlsx, SalesExportFormat.TallyXml }.Contains(query.Format, StringComparer.OrdinalIgnoreCase))
            return Error.Validation("Export.UnsupportedFormat", "The requested export format is not supported.");
        if (!new[] { SalesExportLevel.Summary, SalesExportLevel.LineItems }.Contains(datasetLevel, StringComparer.OrdinalIgnoreCase))
            return Error.Validation("Export.UnsupportedLevel", "The requested export level is not supported.");

        SalesExportDatasetDto dataset;
        await using (var snapshot = await _readStore.OpenSnapshotAsync(cancellationToken))
        {
            var counts = await snapshot.CountAsync(shop.Id, new(startDate, endDate), datasetLevel, query.Format, cancellationToken);
            var isPdf = string.Equals(query.Format, SalesExportFormat.Pdf, StringComparison.OrdinalIgnoreCase);
            var limit = isPdf ? ExportReadLimits.PdfRows : ExportReadLimits.FileRows;
            if (counts.OutputRows > limit)
                return isPdf ? Errors.Export.PdfRowLimitExceeded((int)limit, counts.OutputRows, datasetLevel, query.Format) : Errors.Export.RowLimitExceeded(limit, counts.OutputRows, datasetLevel, query.Format);
            if (counts.SourceRows > ExportReadLimits.SourceRows)
                return Errors.Export.ReadLimitExceeded(ExportReadLimits.SourceRows, counts.SourceRows, datasetLevel, query.Format);
            dataset = await _datasetBuilder.BuildAsync(snapshot, shop, user, startDate, endDate, datasetLevel, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();

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
