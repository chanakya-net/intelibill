import { HttpErrorResponse } from '@angular/common/http';
import { isUuid } from './checkout.models';

export type PaymentState = 'NotStarted' | 'AwaitingPayment' | 'PendingVerification' | 'Verified' | 'Failed';
export type SimulatorOutcome = 'Captured' | 'Timeout' | 'Delayed' | 'Failed';
export type FulfilmentState = 'NotFulfilled' | 'Pending' | 'Fulfilled' | 'NeedsReview';
export type AccessState = 'NotApplicable' | 'Applying' | 'Applied' | 'NeedsReview';

/** Provider-neutral launcher instruction; never a script URL or provider identifier. */
export interface CheckoutLaunch {
  readonly flow: string;
  readonly parameters: Readonly<Record<string, string>>;
}

export interface CheckoutPayment {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly orderId: string;
  readonly attemptId: string | null;
  readonly payment: PaymentState;
  readonly fulfilment: FulfilmentState;
  readonly access: AccessState;
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly startedAt: string | null;
  readonly verifiedAt: string | null;
  readonly termId: string | null;
  readonly termStartsAt: string | null;
  readonly termEndsAt: string | null;
  readonly shopsTotal: number;
  readonly shopsApplied: number;
  readonly canStart: boolean;
  readonly launch: CheckoutLaunch | null;
  readonly restorationHeld?: boolean;
  readonly serviceRestoration?: {
    readonly count: number;
    readonly grantId: string;
    readonly startsAt: string;
    readonly endsAt: string;
  };
  readonly manualPayment?: { readonly payment: 'Verified'; readonly verifiedAt: string } | null;
}

export const SIMULATOR_FLOW = 'development-simulator';
const PAYMENTS: readonly string[] = ['NotStarted', 'AwaitingPayment', 'PendingVerification', 'Verified', 'Failed'];
const FULFILMENTS: readonly string[] = ['NotFulfilled', 'Pending', 'Fulfilled', 'NeedsReview'];
const ACCESS: readonly string[] = ['NotApplicable', 'Applying', 'Applied', 'NeedsReview'];

export function isCheckoutPayment(value: unknown, account: string, order: string): value is CheckoutPayment {
  const p = value as CheckoutPayment | null;
  return !!p && typeof p === 'object' && p.schemaVersion === 1 && p.accountId === account && p.orderId === order &&
    hasValidSource(p) && hasValidRestoration(p) && PAYMENTS.includes(p.payment) &&
    FULFILMENTS.includes(p.fulfilment) && ACCESS.includes(p.access) && Number.isSafeInteger(p.amountPaise) &&
    p.amountPaise > 0 && p.currency === 'INR' && Number.isInteger(p.shopsTotal) && Number.isInteger(p.shopsApplied) &&
    p.shopsApplied >= 0 && p.shopsApplied <= p.shopsTotal && typeof p.canStart === 'boolean' &&
    (p.fulfilment !== 'Pending' || ((p.payment === 'Verified' || !!p.manualPayment) && !p.canStart && p.launch === null)) &&
    (!p.canStart || p.payment === 'NotStarted') &&
    (p.manualPayment != null || p.payment !== 'Failed' || (p.fulfilment === 'NotFulfilled' && p.access === 'NotApplicable' &&
      p.verifiedAt === null && p.termId === null)) &&
    (p.launch === null || (p.payment === 'AwaitingPayment' && typeof p.launch?.flow === 'string' &&
      !!p.launch.parameters && typeof p.launch.parameters === 'object'));
}

function hasValidRestoration(payment: CheckoutPayment): boolean {
  const { restorationHeld, serviceRestoration } = payment;
  return (restorationHeld === undefined || restorationHeld === false || restorationHeld === true &&
      payment.fulfilment === 'NeedsReview' && (payment.payment === 'Verified' || payment.manualPayment != null)) &&
    (serviceRestoration === undefined || serviceRestoration !== null && payment.fulfilment === 'Fulfilled' &&
      Number.isSafeInteger(serviceRestoration.count) && serviceRestoration.count > 0 && isUuid(serviceRestoration.grantId) &&
      typeof serviceRestoration.startsAt === 'string' && typeof serviceRestoration.endsAt === 'string' &&
      Date.parse(serviceRestoration.endsAt) > Date.parse(serviceRestoration.startsAt));
}

function hasValidSource(payment: CheckoutPayment): boolean {
  const manual = payment.manualPayment;
  if (manual == null)
    return payment.payment === 'NotStarted' ? payment.attemptId === null : isUuid(payment.attemptId);

  if (manual.payment !== 'Verified' || typeof manual.verifiedAt !== 'string' ||
    !/(Z|\+00:00)$/.test(manual.verifiedAt) || !(Date.parse(manual.verifiedAt) > 0) ||
    payment.canStart || payment.launch !== null || payment.fulfilment === 'NotFulfilled' ||
    ((payment.fulfilment === 'Fulfilled') !== (payment.access !== 'NotApplicable'))) return false;

  return payment.attemptId === null
    ? payment.payment === 'Verified' && payment.startedAt === null && payment.verifiedAt === manual.verifiedAt
    : isUuid(payment.attemptId) && payment.payment !== 'NotStarted' && typeof payment.startedAt === 'string' &&
      ((payment.payment === 'Verified') === (typeof payment.verifiedAt === 'string'));
}

export type PaymentProblem = 'unavailable' | 'unsupported' | 'expired' | 'changed' | 'denied';

export function paymentFailure(error: unknown): PaymentProblem {
  if (!(error instanceof HttpErrorResponse)) return 'unavailable';
  if (error.status === 401 || error.status === 403) return 'denied';
  switch (error.error?.code) {
    case 'Billing.PaymentUnsupported': return 'unsupported';
    case 'Billing.CheckoutExpired': return 'expired';
    case 'Billing.CheckoutSourceChanged': return 'changed';
    default: return 'unavailable';
  }
}
