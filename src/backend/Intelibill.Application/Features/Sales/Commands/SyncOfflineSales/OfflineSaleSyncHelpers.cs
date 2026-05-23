using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ErrorOr;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal static class OfflineSaleSyncHelpers
{
    private const string StatusDuplicate = "duplicate";
    private const string StatusFailed = "failed";
    private const string StatusNeedsReview = "NeedsReview";

    internal static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static OfflineSaleSyncResultDto BuildDuplicateResult(string clientSaleId, Sale sale) =>
        new(clientSaleId, StatusDuplicate, sale.Id, sale.InvoiceNumber, [])
        {
            Warnings = sale.Warnings,
        };

    internal static OfflineSaleSyncResultDto BuildNeedsReviewResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusNeedsReview, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    internal static OfflineSaleSyncResultDto BuildErrorResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusFailed, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    internal static Error GetSaveFailureError(DbUpdateException exception) =>
        ContainsExceptionText(exception, "ix_sales_shop_id_invoice_number")
            ? Errors.Sale.InvoiceNumberAlreadyUsed
            : Errors.General.Unexpected("Offline sale could not be synced. Please retry.");

    internal static bool ContainsExceptionText(Exception exception, string text)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
