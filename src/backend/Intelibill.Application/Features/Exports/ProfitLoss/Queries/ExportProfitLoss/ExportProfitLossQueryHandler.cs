using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Exports.ProfitLoss.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Wolverine.Attributes;

namespace Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

[WolverineHandler]
public sealed class ExportProfitLossQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ProfitLossReportBuilder reportBuilder,
    IProfitLossExcelExportRenderer excelRenderer)
{
    public async Task<ErrorOr<ProfitLossExportResult>> Handle(
        ExportProfitLossQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
        {
            return Errors.General.NotFound(nameof(user), query.UserId);
        }

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
        {
            return Errors.Shop.ShopNotFound;
        }

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
        {
            return Errors.Shop.MembershipNotFound;
        }

        if (membership.Role == ShopRole.Staff)
        {
            return Errors.Export.UserIsNotOwnerOrManager;
        }

        var report = await reportBuilder.BuildAsync(query.ShopId, query.From, query.To, query.Type, query.Search, cancellationToken);

        var dataset = new ProfitLossExportDatasetDto(
            new ProfitLossExportMetadataDto(
                shop.Name,
                shop.Address,
                $"{user.FirstName} {user.LastName}".Trim(),
                DateTimeOffset.UtcNow,
                report.From,
                report.To,
                report.Type,
                report.Search,
                query.Format ?? "xlsx"),
            new ProfitLossExportSummaryDto(
                report.Summary.NetProfitAfterTax,
                report.Summary.RevenueIncludingTax,
                report.Summary.TotalCost,
                report.Summary.AverageMarginPercent,
                report.Summary.InvoiceCount,
                report.Summary.ReturnCount,
                report.Summary.AdjustmentCount),
            report.Items.Select(row => new ProfitLossExportRowDto(
                row.ReferenceNumber,
                row.OccurredAt,
                row.RowType,
                row.PartyName,
                row.TotalCost,
                row.WastageCost,
                row.RevenueBeforeTax,
                row.RevenueAfterTax,
                row.ProfitBeforeTax,
                row.ProfitAfterTax,
                row.MarginPercent)).ToList());

        return await excelRenderer.RenderAsync(dataset, cancellationToken);
    }
}
