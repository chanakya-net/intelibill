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
});
