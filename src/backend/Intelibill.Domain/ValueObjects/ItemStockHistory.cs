namespace Intelibill.Domain.ValueObjects;

public sealed record ItemStockHistory(bool HasMovements)
{
    public static ItemStockHistory None { get; } = new(false);
}
