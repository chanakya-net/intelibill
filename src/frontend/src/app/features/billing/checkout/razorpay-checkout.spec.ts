import { afterEach, describe, expect, it, vi } from 'vitest';
import { openRazorpayCheckout, razorpayLaunch, RAZORPAY_SCRIPT_URL } from './razorpay-checkout';

const launch = { keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: 27000, currency: 'INR' as const };

afterEach(() => {
  Reflect.deleteProperty(window, 'Razorpay');
  vi.restoreAllMocks();
});

describe('Razorpay checkout opener', () => {
  it('loads only the fixed provider script and hides instalment methods', async () => {
    const options: Record<string, unknown>[] = [];
    vi.spyOn(document.head, 'appendChild').mockImplementation(node => {
      const script = node as HTMLScriptElement;
      expect(script.src).toBe(RAZORPAY_SCRIPT_URL);
      (window as unknown as { Razorpay: unknown }).Razorpay = class {
        constructor(o: Record<string, unknown>) { options.push(o); }
        open() { (options[0]['handler'] as (r: unknown) => void)({
          razorpay_payment_id: 'pay_abc', razorpay_order_id: 'order_abc123', razorpay_signature: 'f'.repeat(64) }); }
      };
      script.onload?.(new Event('load'));
      return node;
    });
    const outcome = await openRazorpayCheckout(launch);
    expect(outcome).toEqual({ kind: 'success', proof: {
      razorpay_payment_id: 'pay_abc', razorpay_order_id: 'order_abc123', razorpay_signature: 'f'.repeat(64) } });
    expect(options[0]).toMatchObject({ key: 'rzp_test_fixture', order_id: 'order_abc123', amount: 27000, currency: 'INR' });
    expect(JSON.stringify(options[0]['config'])).toContain('emi');
  });

  it('treats a callback for another order or a malformed callback as no proof', async () => {
    (window as unknown as { Razorpay: unknown }).Razorpay = class {
      constructor(private readonly o: Record<string, unknown>) {}
      open() { (this.o['handler'] as (r: unknown) => void)({
        razorpay_payment_id: 'pay_abc', razorpay_order_id: 'order_other', razorpay_signature: 'f'.repeat(64) }); }
    };
    expect(await openRazorpayCheckout(launch)).toEqual({ kind: 'cancelled' });
  });

  it('rejects launch parameters outside the allow-list', () => {
    expect(razorpayLaunch({ flow: 'development-simulator', parameters: {} })).toBeNull();
    expect(razorpayLaunch(null)).toBeNull();
  });
});
