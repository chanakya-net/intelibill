// Matches PaymentConfirmationsV1: every named shop allowed by the accepted quote must fit.
export const MAX_CONFIRMATION_BYTES = 8192 + 100_000 * (250 * 4 + 128);
export const CONFIRMATION_PAGE_SIZE = 25;
export interface PaymentConfirmation {
  readonly confirmationId: string;
  readonly accountId: string;
  readonly orderId: string;
  readonly source: 'Online' | 'Manual';
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly verifiedAt: string;
  readonly issuedAt: string;
  readonly issuanceBasis: 'Verification' | 'Retained';
}
const uuid = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
export function confirmations(value: unknown, account: string, order: string): readonly PaymentConfirmation[] {
  if (!Array.isArray(value) || value.length > CONFIRMATION_PAGE_SIZE || !value.every((r: PaymentConfirmation) => r && uuid.test(r.confirmationId) &&
      r.accountId === account && r.orderId === order && ['Online', 'Manual'].includes(r.source) &&
      ['Verification', 'Retained'].includes(r.issuanceBasis) && Number.isSafeInteger(r.amountPaise) &&
      r.amountPaise > 0 && r.amountPaise <= 1e12 && r.currency === 'INR' &&
      Number.isFinite(Date.parse(r.verifiedAt)) && Number.isFinite(Date.parse(r.issuedAt)) &&
      (r.issuanceBasis === 'Retained' || Date.parse(r.issuedAt) >= Date.parse(r.verifiedAt))) || new Set(value.map(r => r.confirmationId)).size !== value.length)
    throw new Error('Invalid payment confirmations');
  return value;
}
