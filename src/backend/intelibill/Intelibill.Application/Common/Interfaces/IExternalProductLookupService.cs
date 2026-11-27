using ErrorOr;

namespace Intelibill.Application.Common.Interfaces;

public interface IExternalProductLookupService
{
    Task<ErrorOr<ExternalProductLookupResult?>> LookupByBarcodeAsync(
        string barcode,
        string? authorizationHeader,
        CancellationToken cancellationToken);
}

public sealed record ExternalProductLookupResult(
    string ProductName,
    string? Description,
    string? Uom);
