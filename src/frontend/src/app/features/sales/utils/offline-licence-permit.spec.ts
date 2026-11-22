import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

import {
  decodeOfflineLicencePermitToken,
  isValidLeaseLicenceEvidence,
  verifyDeliveredOfflineLicencePermit,
  type OfflineLicencePermitBinding,
} from './offline-licence-permit';

const PERMIT_FIXTURE_PATH = resolve(process.cwd(), '../../tests/contracts/licensing/offline-permit-v1.json');
const DELIVERY_FIXTURE_PATH = resolve(process.cwd(), '../../tests/contracts/licensing/offline-permit-delivery-v1.json');

interface PermitFixture {
  readonly permit: {
    readonly token: string;
    readonly fields: Record<string, unknown>;
  };
  readonly grant: {
    readonly token: string;
  };
}

interface DeliveryFixture {
  readonly leaseEvidence: {
    readonly version: number;
    readonly grantId: string;
    readonly grantSha256: string;
    readonly keyId: string;
  };
  readonly issuedRecord: {
    readonly type: string;
    readonly licencePermit: Record<string, unknown>;
  };
  readonly refusedRecord: {
    readonly type: string;
    readonly licencePermit: Record<string, unknown>;
  };
}

const permitFixture = JSON.parse(readFileSync(PERMIT_FIXTURE_PATH, 'utf8')) as PermitFixture;
const deliveryFixture = JSON.parse(readFileSync(DELIVERY_FIXTURE_PATH, 'utf8')) as DeliveryFixture;

function makeBinding(overrides: Partial<OfflineLicencePermitBinding> = {}): OfflineLicencePermitBinding {
  const fields = permitFixture.permit.fields;
  return {
    shopId: fields['shopId'] as string,
    userId: fields['userId'] as string,
    deviceId: fields['deviceId'] as string,
    fiscalYear: '2026-27',
    leaseId: fields['leaseId'] as string,
    leaseExpiresAt: '2026-11-29T11:59:00.000000Z',
    grantId: fields['grantId'] as string,
    grantSha256: fields['grantSha256'] as string,
    tillSessionId: null,
    ...overrides,
  };
}

function encodeBase64Url(value: string): string {
  return Buffer.from(value, 'utf8').toString('base64url');
}

function mutateTokenPayload(token: string, patch: Record<string, unknown>): string {
  const [header, payloadB64, signature] = token.split('.');
  const decoded = JSON.parse(Buffer.from(payloadB64, 'base64url').toString('utf8')) as Record<string, unknown>;
  return `${header}.${encodeBase64Url(JSON.stringify({ ...decoded, ...patch }))}.${signature}`;
}

describe('offline-licence-permit utils', () => {
  it('validates lease licence evidence from the shared delivery fixture', () => {
    expect(isValidLeaseLicenceEvidence(deliveryFixture.leaseEvidence)).toBe(true);
    expect(isValidLeaseLicenceEvidence({ ...deliveryFixture.leaseEvidence, version: 2 })).toBe(false);
    expect(isValidLeaseLicenceEvidence({ ...deliveryFixture.leaseEvidence, grantSha256: 'not-hex' })).toBe(false);
    expect(isValidLeaseLicenceEvidence(null)).toBe(false);
  });

  it('decodes P05-01 real signed permit vector and rejects grant tokens and malformed headers', () => {
    const decoded = decodeOfflineLicencePermitToken(permitFixture.permit.token);
    expect(decoded).not.toBeNull();
    expect(decoded!.keyId).toBe(deliveryFixture.leaseEvidence.keyId);
    expect(decoded!.payload.permitId).toBe(permitFixture.permit.fields['permitId']);
    expect(decoded!.payload.validUntil).toBe('2026-11-22T12:00:00.000000Z');

    // A grant token carries a distinct typ and must never decode as a permit.
    expect(decodeOfflineLicencePermitToken(permitFixture.grant.token)).toBeNull();
    expect(decodeOfflineLicencePermitToken('not-a-token')).toBeNull();
    expect(decodeOfflineLicencePermitToken(`${permitFixture.permit.token}=`)).toBeNull();
  });

  it('accepts the shared delivery issued record when every binding field agrees', () => {
    const binding = makeBinding();
    const fields = permitFixture.permit.fields;
    const verified = verifyDeliveredOfflineLicencePermit(deliveryFixture.issuedRecord.licencePermit, {
      binding,
      snapshotId: fields['snapshotId'] as string,
      // +00:00 offset form from System.Text.Json must match canonical Z microsecond timestamp.
      snapshotCompletedAt: '2026-11-22T11:59:30.123456+00:00',
      sessionId: fields['sessionId'] as string,
    });

    expect(verified).not.toBeNull();
    expect(verified!.permitId).toBe(fields['permitId']);
    expect(verified!.validUntil).toBe('2026-11-22T12:00:00.000000Z');
    expect(verified!.limitingReason).toBe('AuthorityFreshness');
  });

  it('rejects a refused record and every mutated binding field', () => {
    const fields = permitFixture.permit.fields;
    const baseExpected = {
      binding: makeBinding(),
      snapshotId: fields['snapshotId'] as string,
      snapshotCompletedAt: fields['snapshotCompletedAt'] as string,
      sessionId: fields['sessionId'] as string,
    };
    const issued = deliveryFixture.issuedRecord.licencePermit;

    expect(verifyDeliveredOfflineLicencePermit(deliveryFixture.refusedRecord.licencePermit, baseExpected)).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit({ ...issued, version: 2 }, baseExpected)).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit({ ...issued, keyId: 'other-key' }, baseExpected)).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit({ ...issued, validUntil: '2026-11-23T12:00:00.000000Z' }, baseExpected)).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, snapshotId: 'other-snap' })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, snapshotCompletedAt: '2026-11-22T11:59:30.123457Z' })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, sessionId: 'other-session' })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ shopId: 'other-shop' }) })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ userId: 'other-user' }) })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ deviceId: 'Counter-1' }) })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ leaseId: 'other-lease' }) })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ grantId: 'other-grant' }) })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ grantSha256: '0'.repeat(64) }) })).toBeNull();
    expect(verifyDeliveredOfflineLicencePermit(issued, { ...baseExpected, binding: makeBinding({ leaseExpiresAt: '2026-11-22T11:59:59.999999Z' }) })).toBeNull();

    const reversedWindow = mutateTokenPayload(permitFixture.permit.token, { validUntil: '2026-11-22T11:59:30.123456Z' });
    expect(
      verifyDeliveredOfflineLicencePermit(
        { ...issued, signedPermit: reversedWindow, validUntil: '2026-11-22T11:59:30.123456Z' },
        baseExpected,
      ),
    ).toBeNull();
  });
});
