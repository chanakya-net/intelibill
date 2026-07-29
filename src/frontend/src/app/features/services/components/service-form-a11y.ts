import type { AbstractControl } from '@angular/forms';
import type { InputNumberPassThrough } from 'primeng/types/inputnumber';

/**
 * PrimeNG `InputNumber` renders its own native spinbutton, so `aria-invalid` has to be
 * pushed onto that inner input through the pass-through API. Both objects are shared
 * constants so the binding keeps a stable identity across change detection, and the
 * valid variant sends `null` so the attribute is removed instead of left behind.
 */
const INVALID_NUMERIC_FIELD_PT: InputNumberPassThrough = Object.freeze({
  pcInputText: { root: { 'aria-invalid': 'true' } },
});

const VALID_NUMERIC_FIELD_PT: InputNumberPassThrough = Object.freeze({
  pcInputText: { root: { 'aria-invalid': null } },
});

export function numericFieldPt(control: AbstractControl): InputNumberPassThrough {
  return control.invalid && control.touched ? INVALID_NUMERIC_FIELD_PT : VALID_NUMERIC_FIELD_PT;
}
