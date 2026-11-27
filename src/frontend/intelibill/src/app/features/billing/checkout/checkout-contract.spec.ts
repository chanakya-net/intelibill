import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { isCheckoutPayment } from './checkout-payment.models';
import { isHash, shopSetHash } from './checkout.models';

describe('checkout-orders-v1', () => {
  it('accepts neutral unpaid routing review while withholding a launcher and replacement checkout', () => {
    const contract = JSON.parse(readFileSync(resolve(process.cwd(), '../../../tests/contracts/licensing/provider-routing-v1.json'), 'utf8'));
    const review = contract.unverifiedReview;
    expect(isCheckoutPayment(review, review.accountId, review.orderId)).toBe(true);
    expect(review.verifiedAt).toBeNull();
    expect(review.canStart).toBe(false);
    expect(review.launch).toBeNull();
    expect(Object.keys(review).some(key => /provider|configuration/i.test(key))).toBe(false);
  });

  it('uses the same bare shop-set digest and full INR amount as licensing', async () => {
    const contract = JSON.parse(readFileSync(resolve(process.cwd(), '../../../tests/contracts/licensing/checkout-orders-v1.json'), 'utf8'));
    expect(await shopSetHash(contract.create.shopIds)).toBe(contract.hashes.shops);
    expect(await shopSetHash([...contract.create.shopIds].reverse())).toBe(contract.hashes.shops);
    expect(isHash(contract.accept.snapshotHash)).toBe(true);
    expect(contract.evaluation.amountPaise / 100).toBe(270);
    expect(contract.evaluation.currency).toBe('INR');
    expect(Date.parse(contract.evaluation.expiresAt) - Date.parse(contract.evaluation.createdAt)).toBe(15 * 60 * 1000);
  });
});
