namespace Intelibill.Domain.ValueObjects;

public sealed record QuantityUnit(string Code, int Scale, string DocumentUnit, IReadOnlyList<string> Aliases);

/// <summary>Units catalog v1, pinned by tests/contracts/quantity-units-v1.json.</summary>
public static class QuantityUnits
{
    public const int MaxScale = 3;

    private const string CustomWholeDocumentUnit = "NOS";
    private const string CustomFractionalDocumentUnit = "OTH";

    public static IReadOnlyList<QuantityUnit> All { get; } =
    [
        new("PCS", 0, "NOS", ["PCS", "PC", "PIECE", "PIECES", "NOS", "NO", "NUMBER", "NUMBERS", "UNIT", "UNITS", "EA", "EACH"]),
        new("PKT", 0, "PAC", ["PKT", "PACK", "PACKS", "PACKET", "PACKETS", "PAC"]),
        new("BOX", 0, "BOX", ["BOX", "BOXES"]),
        new("DOZ", 0, "DOZ", ["DOZ", "DOZEN", "DOZENS"]),
        new("PAIR", 0, "PRS", ["PAIR", "PAIRS", "PRS"]),
        new("SET", 0, "SET", ["SET", "SETS"]),
        new("BTL", 0, "BTL", ["BTL", "BOTTLE", "BOTTLES"]),
        new("BAG", 0, "BAG", ["BAG", "BAGS"]),
        new("KG", 3, "KGS", ["KG", "KGS", "KILO", "KILOS", "KILOGRAM", "KILOGRAMS"]),
        new("G", 0, "GMS", ["G", "GM", "GMS", "GRAM", "GRAMS"]),
        new("L", 3, "LTR", ["L", "LTR", "LTRS", "LITRE", "LITRES", "LITER", "LITERS"]),
        new("ML", 0, "MLT", ["ML", "MLT", "MILLILITRE", "MILLILITRES", "MILLILITER", "MILLILITERS"]),
        new("M", 3, "MTR", ["M", "MTR", "MTRS", "METRE", "METRES", "METER", "METERS"]),
        new("CM", 0, "CMS", ["CM", "CMS", "CENTIMETRE", "CENTIMETRES", "CENTIMETER", "CENTIMETERS"]),
    ];

    public static QuantityUnit? Resolve(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var key = label.Trim().ToUpperInvariant();
        foreach (var unit in All)
        {
            if (unit.Aliases.Contains(key))
                return unit;
        }

        return null;
    }

    public static string DocumentUnitFor(string? unitCode, int scale)
    {
        if (unitCode is not null)
        {
            foreach (var unit in All)
            {
                if (string.Equals(unit.Code, unitCode, StringComparison.Ordinal))
                    return unit.DocumentUnit;
            }
        }

        return scale > 0 ? CustomFractionalDocumentUnit : CustomWholeDocumentUnit;
    }
}
