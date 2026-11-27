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

public static class ValidatedSaleLines
{
    /// <summary>The printed unit of each goods item sold, by item id, for the issued document.</summary>
    public static IReadOnlyDictionary<Guid, string> DocumentUnitsOf(IEnumerable<ValidatedSaleLine> lines) =>
        lines
            .Where(l => l.LineType == SaleLineType.Goods && l.Item is not null)
            .GroupBy(l => l.Item!.Id)
            .ToDictionary(g => g.Key, g => g.First().Item!.DocumentUnit);
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
