import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { isHash, shopSetHash } from './checkout.models';

describe('checkout-orders-v1', () => {
  it('uses the same bare shop-set digest and full INR amount as licensing', async () => {
    const contract = JSON.parse(readFileSync(resolve(process.cwd(), '../../tests/contracts/licensing/checkout-orders-v1.json'), 'utf8'));
    expect(await shopSetHash(contract.create.shopIds)).toBe(contract.hashes.shops);
    expect(await shopSetHash([...contract.create.shopIds].reverse())).toBe(contract.hashes.shops);
    expect(isHash(contract.accept.snapshotHash)).toBe(true);
    expect(contract.evaluation.amountPaise / 100).toBe(270);
    expect(contract.evaluation.currency).toBe('INR');
    expect(Date.parse(contract.evaluation.expiresAt) - Date.parse(contract.evaluation.createdAt)).toBe(15 * 60 * 1000);
  });
});
