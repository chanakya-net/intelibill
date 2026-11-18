import { afterEach, describe, expect, it, vi } from 'vitest';
import { openRazorpayCheckout, razorpayLaunch, RAZORPAY_SCRIPT_URL } from './razorpay-checkout';

const launch = { keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: 27000, currency: 'INR' as const, methods: ['card', 'upi'] };

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
    expect(options[0]['retry']).toEqual({ enabled: false });
  });

  it('closes the window and reports a hosted payment failure without offering a retry in it', async () => {
    const close = vi.fn();
    const handlers = new Map<string, (response: unknown) => void>();
    (window as unknown as { Razorpay: unknown }).Razorpay = class {
      on(event: string, handler: (response: unknown) => void) { handlers.set(event, handler); }
      open() { handlers.get('payment.failed')?.({ error: { code: 'BAD_REQUEST_ERROR', metadata: { payment_id: 'pay_abc' } } }); }
      close() { close(); }
    };
    expect(await openRazorpayCheckout(launch)).toEqual({ kind: 'failed' });
    expect(close).toHaveBeenCalled();
  });

  it.each(['constructor', 'open'])('rejects and closes a partially opened window when %s throws', async stage => {
    const close = vi.fn();
    (window as unknown as { Razorpay: unknown }).Razorpay = class {
      constructor() { if (stage === 'constructor') throw new Error('broken'); }
      on() { /* registered */ }
      open() { throw new Error('broken'); }
      close() { close(); }
    };
    await expect(openRazorpayCheckout(launch)).rejects.toThrow('broken');
    expect(close).toHaveBeenCalledTimes(stage === 'open' ? 1 : 0);
  });

  it.each([
    [['upi'], 'upi'],
    [['card', 'netbanking'], 'card,netbanking'],
  ])('shows only the pinned method set %j and suppresses every default block', async (methods, declared) => {
    const options: Record<string, unknown>[] = [];
    (window as unknown as { Razorpay: unknown }).Razorpay = class {
      constructor(o: Record<string, unknown>) { options.push(o); }
      open() { (options[0]['modal'] as { ondismiss: () => void }).ondismiss(); }
    };
    const parsed = razorpayLaunch({ flow: 'razorpay-standard', parameters: {
      keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: '27000', currency: 'INR', methods: declared } });
    expect(parsed?.methods).toEqual(methods);
    await openRazorpayCheckout(parsed!);
    expect(options[0]['config']).toEqual({ display: {
      blocks: { pinned: { name: 'Pay the full amount', instruments: methods.map(method => ({ method })) } },
      sequence: ['block.pinned'],
      preferences: { show_default_blocks: false },
      hide: [{ method: 'emi' }, { method: 'paylater' }, { method: 'cardless_emi' }],
    } });
  });

  it.each(['', 'emi', 'upi,emi', 'upi,upi', 'UPI', 'upi;card', 'bank_transfer'])('refuses the declared method set %j', methods => {
    expect(razorpayLaunch({ flow: 'razorpay-standard', parameters: {
      keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: '27000', currency: 'INR', methods } })).toBeNull();
  });

  it.each([
    ['another order', { razorpay_payment_id: 'pay_abc', razorpay_order_id: 'order_other', razorpay_signature: 'f'.repeat(64) }],
    ['a missing signature', { razorpay_payment_id: 'pay_abc', razorpay_order_id: 'order_abc123' }],
    ['a malformed payment id', { razorpay_payment_id: 'pay abc', razorpay_order_id: 'order_abc123', razorpay_signature: 'f'.repeat(64) }],
    ['no response', null],
  ])('reports a success callback for %s as an unverified claim, distinct from a dismissal', async (_case, response) => {
    (window as unknown as { Razorpay: unknown }).Razorpay = class {
      constructor(private readonly o: Record<string, unknown>) {}
      open() { (this.o['handler'] as (r: unknown) => void)(response); }
    };
    expect(await openRazorpayCheckout(launch)).toEqual({ kind: 'unverified' });
  });

  it('reports a dismissed window as cancelled', async () => {
    (window as unknown as { Razorpay: unknown }).Razorpay = class {
      constructor(private readonly o: Record<string, unknown>) {}
      open() { (this.o['modal'] as { ondismiss: () => void }).ondismiss(); }
    };
    expect(await openRazorpayCheckout(launch)).toEqual({ kind: 'cancelled' });
  });

  it('rejects launch parameters outside the allow-list', () => {
    expect(razorpayLaunch({ flow: 'razorpay-standard', parameters: {
      keyId: 'rzp_test_fixture', orderId: 'order_abc123', amount: '27000', currency: 'INR' } })).toBeNull();
    expect(razorpayLaunch({ flow: 'development-simulator', parameters: {} })).toBeNull();
    expect(razorpayLaunch(null)).toBeNull();
  });
});
