using ErrorOr;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Sales.Services;

public sealed record MutatedSaleLine(
    SaleItem SaleItem,
    StockTransaction StockTransaction,
    decimal CalculatedTax);

public interface ISaleInventoryMutator
{
    Task<ErrorOr<MutatedSaleLine>> MutateAsync(
        Guid shopId,
        string invoiceNumber,
        ValidatedSaleLine validatedLine,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
