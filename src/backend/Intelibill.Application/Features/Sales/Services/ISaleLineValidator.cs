using ErrorOr;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services;

public sealed record ValidatedSaleLine(
    RecordSaleItemCommand Command,
    SaleLineType LineType,
    Item? Item,
    InventoryBatch? Batch,
    Domain.Entities.Inventory? Inventory,
    Service? Service,
    bool HasPriceMismatch)
{
    public ValidatedSaleLine(
        RecordSaleItemCommand Command,
        Item Item,
        InventoryBatch Batch,
        bool HasPriceMismatch)
        : this(Command, SaleLineType.Goods, Item, Batch, null, null, HasPriceMismatch)
    {
    }

    public ValidatedSaleLine(
        RecordSaleItemCommand Command,
        Item Item,
        InventoryBatch Batch,
        Domain.Entities.Inventory Inventory,
        bool HasPriceMismatch)
        : this(Command, SaleLineType.Goods, Item, Batch, Inventory, null, HasPriceMismatch)
    {
    }
}

public sealed record SaleLineValidationResult(
    IReadOnlyList<ValidatedSaleLine> Lines,
    IReadOnlyDictionary<Guid, string> ItemNameById);

public interface ISaleLineValidator
{
    Task<ErrorOr<SaleLineValidationResult>> ValidateLinesAsync(
        Guid shopId,
        IReadOnlyList<RecordSaleItemCommand> items,
        List<string> warnings,
        CancellationToken cancellationToken,
        bool allowInsufficientStock = false);
}
