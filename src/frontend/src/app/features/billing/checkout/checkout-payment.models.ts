import { isUuid } from './checkout.models';

export type PaymentState = 'NotStarted' | 'AwaitingPayment' | 'PendingVerification' | 'Verified';
export type FulfilmentState = 'NotFulfilled' | 'Fulfilled' | 'NeedsReview';
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
}

export const SIMULATOR_FLOW = 'development-simulator';
const PAYMENTS: readonly string[] = ['NotStarted', 'AwaitingPayment', 'PendingVerification', 'Verified'];
const FULFILMENTS: readonly string[] = ['NotFulfilled', 'Fulfilled', 'NeedsReview'];
const ACCESS: readonly string[] = ['NotApplicable', 'Applying', 'Applied', 'NeedsReview'];

export function isCheckoutPayment(value: unknown, account: string, order: string): value is CheckoutPayment {
  const p = value as CheckoutPayment | null;
  return !!p && typeof p === 'object' && p.schemaVersion === 1 && p.accountId === account && p.orderId === order &&
    (p.attemptId === null || isUuid(p.attemptId)) && PAYMENTS.includes(p.payment) &&
    FULFILMENTS.includes(p.fulfilment) && ACCESS.includes(p.access) && Number.isSafeInteger(p.amountPaise) &&
    p.amountPaise > 0 && p.currency === 'INR' && Number.isInteger(p.shopsTotal) && Number.isInteger(p.shopsApplied) &&
    p.shopsApplied >= 0 && p.shopsApplied <= p.shopsTotal && typeof p.canStart === 'boolean' &&
    (p.launch === null || (typeof p.launch?.flow === 'string' && typeof p.launch.parameters === 'object'));
}
