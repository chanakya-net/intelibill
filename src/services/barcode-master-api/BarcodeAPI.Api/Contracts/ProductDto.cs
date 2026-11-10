namespace BarcodeAPI.Api.Contracts;

public record ProductDto(Guid Id, string Barcode, string Name, DateTime CreatedAt);

