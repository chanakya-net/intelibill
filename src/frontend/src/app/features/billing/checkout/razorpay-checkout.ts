import { InjectionToken } from '@angular/core';
import type { CheckoutLaunch } from './checkout-payment.models';

/** The one script origin the merchant app will ever load for Razorpay; a gateway can never supply another. */
export const RAZORPAY_SCRIPT_URL = 'https://checkout.razorpay.com/v1/checkout.js';
export const RAZORPAY_FLOW = 'razorpay-standard';
const KEYS = ['amount', 'currency', 'keyId', 'methods', 'orderId'] as const;
const SAFE = /^[A-Za-z0-9_-]{1,120}$/;
/** Instalment and deferred methods can never fund a full-price order. */
const HIDDEN_METHODS = ['emi', 'paylater', 'cardless_emi'];
/** Full-payment methods the server may pin; the declared set is the only one the hosted checkout will show. */
const FULL_PAYMENT_METHODS: readonly string[] = ['card', 'netbanking', 'wallet', 'upi'];

export interface RazorpayLaunch {
  readonly keyId: string;
  readonly orderId: string;
  readonly amount: number;
  readonly currency: 'INR';
  readonly methods: readonly string[];
}

export type RazorpayOutcome =
  | { readonly kind: 'success'; readonly proof: Readonly<Record<string, string>> }
  | { readonly kind: 'cancelled' };

/** The signal closes the modal when its owner is torn down; a closed window reports `cancelled`, never success. */
export type RazorpayOpener = (launch: RazorpayLaunch, signal?: AbortSignal) => Promise<RazorpayOutcome>;
export const RAZORPAY_OPENER = new InjectionToken<RazorpayOpener>('RAZORPAY_OPENER', {
  providedIn: 'root',
  factory: () => openRazorpayCheckout,
});

/** Validated public launch parameters, or null when the server declared anything else. */
export function razorpayLaunch(launch: CheckoutLaunch | null): RazorpayLaunch | null {
  if (launch?.flow !== RAZORPAY_FLOW) return null;
  const parameters = launch.parameters ?? {};
  const names = Object.keys(parameters).sort();
  if (names.length !== KEYS.length || names.some((name, index) => name !== KEYS[index])) return null;
  if (KEYS.some(key => key !== 'methods' && !SAFE.test(parameters[key]))) return null;
  const amount = Number(parameters['amount']);
  if (!/^[1-9][0-9]{0,14}$/.test(parameters['amount']) || !Number.isSafeInteger(amount) || parameters['currency'] !== 'INR') return null;
  const methods = pinnedMethods(parameters['methods']);
  return methods ? { keyId: parameters['keyId'], orderId: parameters['orderId'], amount, currency: 'INR', methods } : null;
}

/** A nonempty, duplicate-free subset of the full-payment methods, declared as a comma-separated list. */
function pinnedMethods(declared: string): string[] | null {
  const methods = declared.split(',');
  return methods.every(method => FULL_PAYMENT_METHODS.includes(method)) && new Set(methods).size === methods.length
    ? methods : null;
}

/**
 * Razorpay's documented display configuration: one block holding exactly the pinned methods, with every default
 * block suppressed, so a method the server would refuse to fulfil is never offered.
 */
function displayConfig(methods: readonly string[]) {
  return { display: {
    blocks: { pinned: { name: 'Pay the full amount', instruments: methods.map(method => ({ method })) } },
    sequence: ['block.pinned'],
    preferences: { show_default_blocks: false },
    hide: HIDDEN_METHODS.map(method => ({ method })),
  } };
}

interface RazorpayConstructor {
  new (options: Record<string, unknown>): { open(): void; close?(): void };
}

function loadScript(): Promise<RazorpayConstructor> {
  const existing = (window as unknown as { Razorpay?: RazorpayConstructor }).Razorpay;
  if (existing) return Promise.resolve(existing);
  return new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = RAZORPAY_SCRIPT_URL;
    script.async = true;
    script.onload = () => {
      const loaded = (window as unknown as { Razorpay?: RazorpayConstructor }).Razorpay;
      if (loaded) resolve(loaded); else reject(new Error('checkout-unavailable'));
    };
    script.onerror = () => reject(new Error('checkout-unavailable'));
    document.head.appendChild(script);
  });
}

function proofOf(response: unknown, orderId: string): Record<string, string> | null {
  const value = response as Record<string, unknown> | null;
  const proof: Record<string, string> = {};
  for (const key of ['razorpay_payment_id', 'razorpay_order_id', 'razorpay_signature']) {
    const entry = value?.[key];
    if (typeof entry !== 'string' || !SAFE.test(entry)) return null;
    proof[key] = entry;
  }
  return proof['razorpay_order_id'] === orderId ? proof : null;
}

/**
 * Opens Razorpay Standard Checkout for the server-created order. The callback is only a claim: it is relayed to
 * the server, which verifies the signature against its own persisted order and reads the captured payment itself.
 */
export async function openRazorpayCheckout(launch: RazorpayLaunch, signal?: AbortSignal): Promise<RazorpayOutcome> {
  const Razorpay = await loadScript();
  if (signal?.aborted) return { kind: 'cancelled' };
  return new Promise<RazorpayOutcome>(resolve => {
    const modal = new Razorpay({
      key: launch.keyId,
      order_id: launch.orderId,
      amount: launch.amount,
      currency: launch.currency,
      name: 'InteliBill',
      config: displayConfig(launch.methods),
      handler: (response: unknown) => {
        const proof = proofOf(response, launch.orderId);
        resolve(proof ? { kind: 'success', proof } : { kind: 'cancelled' });
      },
      modal: { ondismiss: () => resolve({ kind: 'cancelled' }) },
    });
    signal?.addEventListener('abort', () => { modal.close?.(); resolve({ kind: 'cancelled' }); }, { once: true });
    modal.open();
  });
}
