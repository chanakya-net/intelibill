// The credit-policy refusals the server answers when a sale's due breaks the customer's policy,
// mapped to cashier messages. Anything not listed keeps the caller's own fallback.

export const CUSTOMER_CREDIT_ERROR_KEYS: Readonly<Record<string, string>> = {
  'Customer.CreditCustomerInactive': 'sales.newSale.credit.errors.inactive',
  'Customer.CreditNotExtended': 'sales.newSale.credit.errors.notExtended',
  'Customer.CreditLimitExceeded': 'sales.newSale.credit.errors.limitExceeded',
  'Customer.CreditOverrideNotPermitted': 'sales.newSale.credit.errors.overrideNotPermitted',
  'Customer.CreditOverrideStale': 'sales.newSale.credit.errors.overrideStale',
  'Customer.CreditOverrideReasonRequired': 'sales.newSale.credit.errors.overrideReasonRequired',
  'Customer.CreditBusy': 'sales.newSale.credit.errors.busy',
};

/** The message key for a credit refusal code, or null when the code is not one. */
export function customerCreditErrorKey(code: unknown): string | null {
  return typeof code === 'string' ? (CUSTOMER_CREDIT_ERROR_KEYS[code] ?? null) : null;
}
