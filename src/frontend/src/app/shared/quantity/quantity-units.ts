/**
 * The units catalog v1, pinned with the server by `tests/contracts/quantity-units-v1.json`.
 *
 * An item keeps its free-text unit label; a label matching an alias (after trim and invariant upper-casing) is that
 * catalog unit, with its fixed precision and the unit code (UQC) its documents print. Any other label is custom: the
 * merchant chooses its precision and documents print NOS for whole units or OTH for fractional ones.
 */
export interface QuantityUnitDefinition {
  readonly code: string;
  /** Decimal places a quantity in this unit may carry (0 = whole units). */
  readonly scale: number;
  /** The unit code printed on issued documents. */
  readonly documentUnit: string;
  readonly aliases: readonly string[];
}

/** Every stored quantity has at most three decimal places. */
export const QUANTITY_SCALE_MAX = 3;

/** The document unit of a custom (uncatalogued) label, by whether it allows fractions. */
export const QUANTITY_CUSTOM_DOCUMENT_UNIT = { whole: 'NOS', fractional: 'OTH' } as const;

export const QUANTITY_UNITS: readonly QuantityUnitDefinition[] = [
  { code: 'PCS', scale: 0, documentUnit: 'NOS', aliases: ['PCS', 'PC', 'PIECE', 'PIECES', 'NOS', 'NO', 'NUMBER', 'NUMBERS', 'UNIT', 'UNITS', 'EA', 'EACH'] },
  { code: 'PKT', scale: 0, documentUnit: 'PAC', aliases: ['PKT', 'PACK', 'PACKS', 'PACKET', 'PACKETS', 'PAC'] },
  { code: 'BOX', scale: 0, documentUnit: 'BOX', aliases: ['BOX', 'BOXES'] },
  { code: 'DOZ', scale: 0, documentUnit: 'DOZ', aliases: ['DOZ', 'DOZEN', 'DOZENS'] },
  { code: 'PAIR', scale: 0, documentUnit: 'PRS', aliases: ['PAIR', 'PAIRS', 'PRS'] },
  { code: 'SET', scale: 0, documentUnit: 'SET', aliases: ['SET', 'SETS'] },
  { code: 'BTL', scale: 0, documentUnit: 'BTL', aliases: ['BTL', 'BOTTLE', 'BOTTLES'] },
  { code: 'BAG', scale: 0, documentUnit: 'BAG', aliases: ['BAG', 'BAGS'] },
  { code: 'KG', scale: 3, documentUnit: 'KGS', aliases: ['KG', 'KGS', 'KILO', 'KILOS', 'KILOGRAM', 'KILOGRAMS'] },
  { code: 'G', scale: 0, documentUnit: 'GMS', aliases: ['G', 'GM', 'GMS', 'GRAM', 'GRAMS'] },
  { code: 'L', scale: 3, documentUnit: 'LTR', aliases: ['L', 'LTR', 'LTRS', 'LITRE', 'LITRES', 'LITER', 'LITERS'] },
  { code: 'ML', scale: 0, documentUnit: 'MLT', aliases: ['ML', 'MLT', 'MILLILITRE', 'MILLILITRES', 'MILLILITER', 'MILLILITERS'] },
  { code: 'M', scale: 3, documentUnit: 'MTR', aliases: ['M', 'MTR', 'MTRS', 'METRE', 'METRES', 'METER', 'METERS'] },
  { code: 'CM', scale: 0, documentUnit: 'CMS', aliases: ['CM', 'CMS', 'CENTIMETRE', 'CENTIMETRES', 'CENTIMETER', 'CENTIMETERS'] },
];

const UNITS_BY_ALIAS: ReadonlyMap<string, QuantityUnitDefinition> = new Map(
  QUANTITY_UNITS.flatMap((unit) => unit.aliases.map((alias) => [alias, unit] as const)),
);

/** The catalog unit a label names, or null for a custom, empty or missing label. */
export function resolveQuantityUnit(label: string | null | undefined): QuantityUnitDefinition | null {
  // toUpperCase is locale-independent (unlike toLocaleUpperCase), matching the server's invariant upper-casing.
  const key = (label ?? '').trim().toUpperCase();
  return key ? (UNITS_BY_ALIAS.get(key) ?? null) : null;
}
