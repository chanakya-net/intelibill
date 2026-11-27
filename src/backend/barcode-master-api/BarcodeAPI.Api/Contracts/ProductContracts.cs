using System.ComponentModel.DataAnnotations;

namespace BarcodeAPI.Api.Contracts;

public record BatchProductItem(
    [Required] string Barcode,
    [Required] string Name);

public record BatchProductRequest(List<BatchProductItem> Products);

public record FailedProductItem(string Barcode, string Name, string Reason);

public record BatchProductResponse(
    int TotalReceived,
    int SuccessfulCount,
    int FailedCount,
    List<FailedProductItem> FailedRecords);

public record ProductLookupRequest(List<string> Barcodes);

public record ProductLookupResponse(
    List<ProductDto> Products,
    List<string> NotFound);
