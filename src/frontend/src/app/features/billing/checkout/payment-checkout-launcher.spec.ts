import { describe, expect, it } from 'vitest';
import { resolvePaymentLauncher } from './payment-checkout-launcher';

describe('resolvePaymentLauncher', () => {
  it('opens the simulator only for the server-declared development flow without parameters', () => {
    expect(resolvePaymentLauncher(null)).toBeNull();
    expect(resolvePaymentLauncher({ flow: 'development-simulator', parameters: {} })).toBe('simulator');
    expect(resolvePaymentLauncher({ flow: 'development-simulator', parameters: { script: 'https://x.test/a.js' } }))
      .toBe('unsupported');
    expect(resolvePaymentLauncher({ flow: 'https://x.test/a.js', parameters: {} })).toBe('unsupported');
  });

  it('opens Razorpay only for its exact allow-listed public parameters', () => {
    const parameters = { keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: '27000', currency: 'INR' };
    expect(resolvePaymentLauncher({ flow: 'razorpay-standard', parameters })).toBe('razorpay');
    expect(resolvePaymentLauncher({ flow: 'razorpay-standard', parameters: { ...parameters, script: 'https://x.test/a.js' } }))
      .toBe('unsupported');
    expect(resolvePaymentLauncher({ flow: 'razorpay-standard', parameters: { ...parameters, orderId: 'https://x.test' } }))
      .toBe('unsupported');
    expect(resolvePaymentLauncher({ flow: 'razorpay-standard', parameters: { ...parameters, amount: '0' } })).toBe('unsupported');
    expect(resolvePaymentLauncher({ flow: 'razorpay-standard', parameters: { ...parameters, currency: 'USD' } })).toBe('unsupported');
    expect(resolvePaymentLauncher({ flow: 'razorpay-standard', parameters: {} })).toBe('unsupported');
  });
});
