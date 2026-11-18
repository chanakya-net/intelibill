export interface CheckoutScope {
  readonly userId: string;
  readonly shopId: string;
  readonly account: string;
}

export type Period = 'Month' | 'Year';

export interface CreateQuote {
  readonly operationKey: string;
  readonly planVersionId: string;
  readonly period: Period;
  readonly shopIds: readonly string[];
  readonly extraShops: number;
  readonly expectedRevision: 1;
  readonly termId: string | null;
}

export interface AcceptQuote {
  readonly operationKey: string;
  readonly expectedRevision: number;
  readonly snapshotHash: string;
  readonly customerTermsVersion: string;
  readonly refundPolicyVersion: string;
  readonly confirmed: true;
}

export interface Quote {
  readonly accountId: string;
  readonly orderId: string;
  readonly revision: number;
  readonly schemaVersion: 1;
  readonly planVersionId: string;
  readonly planName: string;
  readonly planVersion: number;
  readonly period: Period;
  readonly termId: string | null;
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly priceBasis: 'TaxInclusiveFinal';
  readonly capacity: number;
  readonly membersPerShop: number;
  readonly shopCount: number;
  readonly shopHash: string;
  readonly snapshotHash: string;
  readonly customerTermsVersion: string;
  readonly refundPolicyVersion: string;
  readonly billingTimeZone: 'Asia/Kolkata';
  readonly previewStartsAt: string;
  readonly previewEndsAt: string;
  readonly expiresAt: string;
  readonly startRule: 'Fulfilment' | 'PriorPaidThrough' | 'RestoredAccess';
  readonly graceDaysIncluded: number;
  readonly freshStart?: { readonly policy: 'paid-fresh-start/1'; readonly trialShopCount: number; readonly trialSourceHash: string };
  readonly developmentFixture: boolean;
  readonly acceptance: { readonly operationKey: string; readonly snapshotHash: string; readonly acceptedAt: string } | null;
}

export interface CheckoutShop {
  readonly shopId: string;
  readonly name: string;
  readonly termId: string | null;
}

export interface CheckoutShopPage {
  readonly accountId: string;
  readonly orderId: string | null;
  readonly items: readonly CheckoutShop[];
  readonly nextCursor: string | null;
}

export const isUuid = (value: unknown): value is string =>
  typeof value === 'string' && /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/.test(value);
export const isHash = (value: unknown): value is string =>
  typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);

export async function shopSetHash(ids: readonly string[]): Promise<string> {
  const bytes = new TextEncoder().encode([...ids].sort().join(','));
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), n => n.toString(16).padStart(2, '0')).join('');
}
