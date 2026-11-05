import { QUANTITY_SCALE_MAX } from './quantity-units';
import { fitsQuantityScale, formatQuantity, quantityStep } from './quantity-input';

/** A translation key with the parameters its message interpolates. */
export interface QuantityMessage {
  readonly key: string;
  readonly params: { readonly unit: string; readonly decimals: number; readonly step: string };
}

/** The decimal places a quantity entered for a stock row may carry: the item's, or the storage ceiling when unknown. */
export function entryScaleOf(quantityScale: number | null | undefined): number {
  return typeof quantityScale === 'number' && Number.isFinite(quantityScale)
    ? Math.min(Math.max(Math.trunc(quantityScale), 0), QUANTITY_SCALE_MAX)
    : QUANTITY_SCALE_MAX;
}

/**
 * The refusal for a quantity with more decimals than its item's unit allows, naming the unit and the allowed decimals;
 * null when the quantity fits (or none was entered). The quantity itself is never rounded.
 */
export function quantityPrecisionMessage(
  value: unknown,
  unit: string,
  quantityScale: number | null | undefined,
  locale: string,
): QuantityMessage | null {
  const scale = entryScaleOf(quantityScale);
  const quantity = typeof value === 'number' ? value : Number.NaN;
  if (!Number.isFinite(quantity) || fitsQuantityScale(quantity, scale)) {
    return null;
  }

  return {
    key: scale > 0 ? 'common.quantity.errors.tooPrecise' : 'common.quantity.errors.tooPreciseWhole',
    params: { unit, decimals: scale, step: formatQuantity(quantityStep(scale), scale, locale) },
  };
}
