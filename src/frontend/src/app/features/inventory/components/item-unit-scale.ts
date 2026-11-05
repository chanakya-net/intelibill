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
  // The precision the merchant had on a custom unit, brought back when a catalog label is replaced by a custom one
  // (whole numbers when the item never had a custom precision).
  let customScale = 0;
  let initial = true;

  const apply = (label: string): void => {
    const unit = resolveQuantityUnit(label);
    if (unit) {
      // The value an item starts with under a catalog label is the catalog's, not a custom choice.
      if (scale.enabled && !initial) {
        customScale = scale.value;
      }
      scale.setValue(unit.scale, { emitEvent: false });
      scale.disable({ emitEvent: false });
    } else if (scale.disabled) {
      scale.enable({ emitEvent: false });
      scale.setValue(customScale, { emitEvent: false });
    }
  };

  apply(uom.value);
  initial = false;
  if (!resolveQuantityUnit(uom.value)) {
    customScale = scale.value;
  }
  uom.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe(apply);
}

/** The `quantityScale` to send: only a custom label carries one (a catalog label's precision is the catalog's). */
export function quantityScaleToSend(uom: string, scale: number): number | null {
  return resolveQuantityUnit(uom) ? null : scale;
}
