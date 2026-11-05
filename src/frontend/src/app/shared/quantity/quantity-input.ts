import { QUANTITY_SCALE_MAX } from './quantity-units';

/**
 * What a person typed as a quantity, read exactly or refused. A quantity is never rounded, truncated or clamped
 * here: anything the unit cannot hold is refused so the person can correct it.
 */
export type QuantityParseResult =
  | { readonly ok: true; readonly value: number }
  | { readonly ok: false; readonly error: QuantityInputError };

export type QuantityInputError = 'empty' | 'invalid' | 'notPositive' | 'tooPrecise';

/** Integer digits a stored quantity can carry (`numeric(18,3)`); more cannot be held, nor read exactly as a number. */
const MAX_INTEGER_DIGITS = 15;

/**
 * Reads a typed quantity: ASCII digits with at most one decimal separator (the locale's, or a point), nothing else
 * (no grouping, sign, exponent, inner spaces or words). Surrounding whitespace of a pasted value is ignored, and
 * trailing zeros after the separator do not count as precision (`2.000` is a whole number).
 */
export function parseQuantityInput(text: string, locale: string, scale: number): QuantityParseResult {
  const trimmed = (text ?? '').trim();
  if (trimmed === '') {
    return { ok: false, error: 'empty' };
  }

  const negative = trimmed.startsWith('-');
  const unsigned = negative ? trimmed.slice(1) : trimmed;
  const separators = new Set(['.', decimalSeparatorOf(locale)]);
  const parts = splitOnce(unsigned, separators);
  if (!parts) {
    return { ok: false, error: 'invalid' };
  }

  const [integerDigits, fractionDigits] = parts;
  if (integerDigits.replace(/^0+(?=\d)/, '').length > MAX_INTEGER_DIGITS) {
    return { ok: false, error: 'invalid' };
  }

  const significantFraction = fractionDigits.replace(/0+$/, '');
  const value = Number(`${integerDigits || '0'}.${significantFraction || '0'}`);
  if (negative || value === 0) {
    return { ok: false, error: 'notPositive' };
  }

  if (significantFraction.length > clampScale(scale)) {
    return { ok: false, error: 'tooPrecise' };
  }

  return { ok: true, value };
}

/** The smallest quantity a unit of this scale can hold: 1, 0.1, 0.01 or 0.001. */
export function quantityStep(scale: number): number {
  return 1 / 10 ** clampScale(scale);
}

/** Whether a quantity is finite and needs no more decimal places than the scale allows. */
export function fitsQuantityScale(value: number, scale: number): boolean {
  return Number.isFinite(value) && decimalPlacesOf(value) <= clampScale(scale);
}

/** The fewest decimal places that hold this quantity exactly, capped at the storage scale (2.000 → 0, 0.25 → 2). */
export function quantityScaleOf(value: number): number {
  return Number.isFinite(value) ? Math.min(decimalPlacesOf(value), QUANTITY_SCALE_MAX) : 0;
}

/**
 * Removes binary float noise from the result of arithmetic on quantities that already fit the scale (a sum or a
 * difference, e.g. 0.1 + 0.2). Never use it on what a person typed: that is parsed exactly or refused.
 */
export function normalizeQuantity(value: number, scale: number): number {
  return Number(value.toFixed(clampScale(scale)));
}

/**
 * Shows a quantity in the locale's decimal separator, Latin digits and no grouping, so the text reads back through
 * `parseQuantityInput`. At least the unit's scale is kept when the value has more decimals: it is never rounded.
 */
export function formatQuantity(value: number, scale: number, locale: string): string {
  if (!Number.isFinite(value)) {
    return '';
  }

  const fractionDigits = Math.min(20, Math.max(clampScale(scale), decimalPlacesOf(value)));
  const options: Intl.NumberFormatOptions = {
    useGrouping: false,
    minimumFractionDigits: 0,
    maximumFractionDigits: fractionDigits,
    numberingSystem: 'latn',
  };
  try {
    return new Intl.NumberFormat(locale, options).format(value);
  } catch {
    return new Intl.NumberFormat('en', options).format(value);
  }
}

function decimalSeparatorOf(locale: string): string {
  try {
    const parts = new Intl.NumberFormat(locale, { numberingSystem: 'latn' }).formatToParts(1.5);
    return parts.find((part) => part.type === 'decimal')?.value ?? '.';
  } catch {
    return '.';
  }
}

/** The digits before and after the one decimal separator, or null when the text is anything else. */
function splitOnce(text: string, separators: ReadonlySet<string>): readonly [string, string] | null {
  let integerDigits = '';
  let fractionDigits = '';
  let seenSeparator = false;
  for (const character of text) {
    if (character >= '0' && character <= '9') {
      if (seenSeparator) {
        fractionDigits += character;
      } else {
        integerDigits += character;
      }
    } else if (separators.has(character) && !seenSeparator) {
      seenSeparator = true;
    } else {
      return null;
    }
  }

  return integerDigits.length + fractionDigits.length > 0 ? [integerDigits, fractionDigits] : null;
}

/** Decimal places of the shortest text that reads back as this number (0.25 → 2, 1e-7 → 7). */
function decimalPlacesOf(value: number): number {
  const [mantissa, exponent] = String(Math.abs(value)).toLowerCase().split('e');
  const fraction = (mantissa.split('.')[1] ?? '').replace(/0+$/, '');
  return Math.max(0, fraction.length - Number(exponent ?? 0));
}

function clampScale(scale: number): number {
  return Number.isInteger(scale) ? Math.min(Math.max(scale, 0), QUANTITY_SCALE_MAX) : 0;
}
