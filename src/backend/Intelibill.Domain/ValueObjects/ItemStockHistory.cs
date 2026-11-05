namespace Intelibill.Domain.ValueObjects;

public sealed record ItemStockHistory(bool HasMovements, int MaxOnHandScale)
{
    public static ItemStockHistory None { get; } = new(false, 0);
}
