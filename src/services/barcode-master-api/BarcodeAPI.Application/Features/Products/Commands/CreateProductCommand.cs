using BarcodeAPI.Domain.Entities;
using BarcodeAPI.Domain.Interfaces.Repositories;

namespace BarcodeAPI.Application.Features.Products.Commands;

public record CreateProductCommand(string Barcode, string Name);

public static class CreateProductHandler
{
    public static async Task Handle(CreateProductCommand command, IProductRepository repository, CancellationToken ct)
    {
        var existing = await repository.GetByBarcodeAsync(command.Barcode, ct);
        if (existing != null) return;

        var product = new Product(command.Barcode, command.Name);
        await repository.AddAsync(product, ct);
    }
}
