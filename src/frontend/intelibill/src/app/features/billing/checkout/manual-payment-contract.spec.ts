import { describe, expect, it } from 'vitest';
import contract from '../../../../../../../../tests/contracts/licensing/manual-payment-v1.json';
import { isCheckoutPayment } from './checkout-payment.models';

describe('manual checkout status contract', () => {
  it.each(contract.cases)('$name', ({ patch, valid }) => {
    const { accountId, orderId } = contract.online;
    expect(isCheckoutPayment({ ...contract.online, ...patch }, accountId, orderId)).toBe(valid);
  });
});
