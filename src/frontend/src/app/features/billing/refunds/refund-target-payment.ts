import type { CheckoutScope } from '../checkout/checkout.models';
import { CONFIRMATION_PAGE_SIZE, type PaymentConfirmation } from '../payments/payment-confirmation.models';
import type { PaymentConfirmationService } from '../payments/payment-confirmation.service';

export type RefundTargetLookup =
  | { readonly kind: 'found'; readonly payment: PaymentConfirmation }
  | { readonly kind: 'missing' }
  | { readonly kind: 'stalled' }
  | { readonly kind: 'stale' };

/**
 * Pages the order's verified receipts to completion looking for the selected confirmation. The read is the
 * member's current authority over the receipt: it throws when access is refused and reports `missing` when the
 * order has no such receipt. `stale` means the caller's scope moved on; `stalled` that the cursor did not advance.
 */
export async function findRefundTargetPayment(
  confirmations: PaymentConfirmationService, scope: CheckoutScope, order: string, confirmation: string,
  current: () => boolean,
): Promise<RefundTargetLookup> {
  let after: string | undefined;
  const seenCursors = new Set<string>();
  for (;;) {
    const rows = await confirmations.list(scope, order, after);
    if (!current()) return { kind: 'stale' };
    const payment = rows.find(row => row.confirmationId === confirmation && row.orderId === order);
    if (payment) return { kind: 'found', payment };
    if (rows.length < CONFIRMATION_PAGE_SIZE) return { kind: 'missing' };
    const next = rows.at(-1)?.confirmationId;
    if (!next || seenCursors.has(next)) return { kind: 'stalled' };
    seenCursors.add(next);
    after = next;
  }
}
