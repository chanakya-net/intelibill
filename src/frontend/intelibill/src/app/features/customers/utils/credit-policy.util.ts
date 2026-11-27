import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

import { CreditPolicyMode } from '../services/customer.service';

/** The policies an owner or manager can choose. */
export const SELECTABLE_CREDIT_POLICIES: readonly CreditPolicyMode[] = ['NoCredit', 'Enforced'];

export const MAX_CREDIT_TERMS_DAYS = 365;

export function creditPolicyLabelKey(mode: CreditPolicyMode): string {
  return `customers.creditPolicy.modes.${mode}`;
}

/** An enforced policy needs a positive limit; a zero limit never means "unlimited". */
export function enforcedPolicyNeedsLimit(policyControl: string, limitControl: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const mode = group.get(policyControl)?.value as CreditPolicyMode | null;
    const limit = Number(group.get(limitControl)?.value ?? 0);
    return mode === 'Enforced' && !(limit > 0) ? { enforcedCreditRequiresLimit: true } : null;
  };
}
