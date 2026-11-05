import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { FormControl } from '@angular/forms';

import { resolveQuantityUnit } from '../../../shared/quantity/quantity-units';

/** The precisions a custom unit can offer, in decimal places (whole numbers, 0.1, 0.01, 0.001). */
export const ITEM_QUANTITY_SCALE_OPTIONS: readonly { value: number; labelKey: string }[] = [
  { value: 0, labelKey: 'inventory.quantityScale.whole' },
  { value: 1, labelKey: 'inventory.quantityScale.oneDecimal' },
  { value: 2, labelKey: 'inventory.quantityScale.twoDecimals' },
  { value: 3, labelKey: 'inventory.quantityScale.threeDecimals' },
];

/**
 * Keeps the precision control in step with the unit label: a catalog unit fixes its own precision (control shown and
 * disabled), a custom label lets the merchant choose.
 */
export function bindQuantityScaleToUnit(
  uom: FormControl<string>,
  scale: FormControl<number>,
  destroyRef: DestroyRef,
): void {
  const apply = (label: string): void => {
    const unit = resolveQuantityUnit(label);
    if (unit) {
      scale.setValue(unit.scale, { emitEvent: false });
      scale.disable({ emitEvent: false });
    } else if (scale.disabled) {
      scale.enable({ emitEvent: false });
    }
  };

  apply(uom.value);
  uom.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe(apply);
}

/** The `quantityScale` to send: only a custom label carries one (a catalog label's precision is the catalog's). */
export function quantityScaleToSend(uom: string, scale: number): number | null {
  return resolveQuantityUnit(uom) ? null : scale;
}
