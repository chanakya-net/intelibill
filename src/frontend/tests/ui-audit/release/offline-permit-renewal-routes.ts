import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { DEVICE_ID, FISCAL_YEAR, GRANT_SHA256, SELLER_PROFILE, batchRecord, canonicalInstant,
  createFixtureSigner, permitFieldsFor, type FixtureSigner } from './offline-permit-finalization-fixture';
import { SHOP_A } from './purchase-receiving-server';

export const renewedLease = '11111111-1111-4111-8111-111111111112';
const renewedGrant = '22222222-2222-4222-8222-222222222223';

type CandidateMode = 'valid' | 'stale' | 'untrusted' | 'wrong-session' | 'reordered-revision' | 'refused' | 'stock-cap';
export async function renewalRoutes(page: Page, signer: FixtureSigner, verified: number, mode: CandidateMode = 'valid', barrier?: Promise<void>, onStream?: (tab: Page) => void, stockEnd?: string) {
  const routes = onStream ? page.context() : page;
  const counts = { reservations: 0, streams: 0 };
  await routes.route('**/trading-authority*', route => route.fulfill({ json: {
    schemaVersion: 1, shopId: SHOP_A, enforced: true, authorityVerifiedAt: canonicalInstant(verified),
    validUntil: canonicalInstant(verified + 60 * 60_000),
  } }));
  await routes.route('**/invoice-leases/reserve*', route => {
    counts.reservations++;
    return route.fulfill({ json: {
      leaseId: renewedLease, shopId: SHOP_A, deviceId: DEVICE_ID, fiscalYear: FISCAL_YEAR,
      prefix: 'A/2627/', numberPadding: 6, rangeStart: 101, rangeEnd: 200, nextNumber: 101, remainingCount: 100,
      reservedAt: canonicalInstant(verified), expiresAt: canonicalInstant(verified + 86_400_000),
      sellerProfile: SELLER_PROFILE, licenceEvidence: { version: 1, grantId: renewedGrant, grantSha256: GRANT_SHA256, keyId: signer.keyId },
    } });
  });
  await routes.route('**/offline-snapshot/stream*', async route => {
    if (new URL(route.request().url()).searchParams.get('leaseId') !== renewedLease) return route.fallback();
    counts.streams++;
    onStream?.(route.request().frame().page());
    if (barrier) await barrier;
    const signingKey = mode === 'untrusted' ? createFixtureSigner('other') : signer;
    const snapshotId = randomUUID();
    const issuedAt = Math.max(verified, Date.now());
    const completedAt = canonicalInstant(issuedAt);
    const fields = permitFieldsFor(snapshotId, issuedAt, { leaseId: renewedLease, grantId: renewedGrant,
      authorityVerifiedAt: canonicalInstant(mode === 'stale' ? verified - 120_000 : verified), deviceVerifiedAt: canonicalInstant(verified),
      ...(mode === 'stock-cap' ? { limitingReason: 'StockEvidenceExpiry', validUntil: stockEnd! } : {}),
      ...(mode === 'wrong-session' ? { sessionId: randomUUID() } : {}),
      shopRevision: mode === 'reordered-revision' ? 0 : 2, currentShopRevision: mode === 'reordered-revision' ? 0 : 2 });
    const records = [
      { type: 'metadata', metadata: { snapshotId, shopId: SHOP_A, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 1, startedAt: completedAt } },
      { type: 'batch', batch: batchRecord(verified) },
      { type: 'licencePermit', licencePermit: mode === 'refused' ?
        { version: 1, status: 'refused', refusalCode: 'OfflinePermit.AuthorityChanged', leaseId: renewedLease, grantId: renewedGrant, snapshotId } :
        { version: 1, status: 'issued', ...fields, keyId: signingKey.keyId, signedPermit: signingKey.sign(fields) } },
      { type: 'complete', complete: { snapshotId, completedAt, batchCount: 1, customerCount: 0, discountRuleCount: 0, activeLeaseCount: 0, serviceCount: 0 } },
    ];
    return route.fulfill({ contentType: 'application/x-ndjson', body: records.map(r => JSON.stringify(r)).join('\n') + '\n' });
  });
  return counts;
}
