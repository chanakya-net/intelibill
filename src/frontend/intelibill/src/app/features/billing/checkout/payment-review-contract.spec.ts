import { describe, expect, it } from 'vitest';
import base from '../../../../../../../../tests/contracts/licensing/manual-payment-v1.json';
import review from '../../../../../../../../tests/contracts/licensing/payment-review-v1.json';
import { isCheckoutPayment } from './checkout-payment.models';

describe('payment review checkout status contract', () => {
  it.each([review.heldCheckout, review.recoveredCheckout])('keeps the original online and manual statuses valid', patch => {
    const { accountId, orderId, verifiedAt } = base.online;
    expect(isCheckoutPayment({ ...base.online, ...patch }, accountId, orderId)).toBe(true);
    expect(isCheckoutPayment({
      ...base.online, ...patch, attemptId: null, startedAt: null, manualPayment: { payment: 'Verified', verifiedAt },
    }, accountId, orderId)).toBe(true);
  });
});
