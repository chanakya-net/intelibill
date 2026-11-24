import { generateKeyPairSync, sign } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

import {
  decodeOfflineLicencePermitToken,
  isValidLeaseLicenceEvidence,
  verifyDeliveredOfflineLicencePermit,
  type OfflineLicencePermitBinding,
} from './offline-licence-permit';
import { readOfflinePermitPublicKeys, verifyOfflinePermitSignature } from './offline-permit-signature';
import { createPermitSigner, permitFields } from '../../../../tests/sales/offline-permit-signing.fixture';

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

describe('verifyOfflinePermitSignature (#900 vectors, WebCrypto)', () => {
  interface SignedFixture {
    readonly key: { readonly keyId: string; readonly publicKeySpki: string };
    readonly permit: { readonly token: string; readonly fields: Record<string, unknown> };
    readonly negative: readonly { readonly name: string; readonly token: string; readonly error: string }[];
  }
  const fixture = JSON.parse(readFileSync(PERMIT_FIXTURE_PATH, 'utf8')) as SignedFixture;
  const pins = { [fixture.key.keyId]: fixture.key.publicKeySpki };

  it('verifies the signed permit vector against its pinned public key and returns the original bytes', async () => {
    const verified = await verifyOfflinePermitSignature(fixture.permit.token, pins);

    expect(verified?.keyId).toBe(fixture.key.keyId);
    expect(verified?.signedPermit).toBe(fixture.permit.token);
    expect(verified?.payload).toEqual(fixture.permit.fields);
  });

  it('rejects a noncanonical signature segment in the fixed RS256 vector', async () => {
    const token = fixture.permit.token;
    expect(token.endsWith('IA')).toBe(true);
    expect(await verifyOfflinePermitSignature(token.slice(0, -1) + 'B', pins)).toBeNull();
  });

  it('rejects signed noncanonical header and payload encodings', async () => {
    const [, payload] = fixture.permit.token.split('.');
    const pair = generateKeyPairSync('rsa', { modulusLength: 2048 });
    const keys = { x: pair.publicKey.export({ type: 'spki', format: 'der' }).toString('base64') };
    const raw = '{"alg":"RS256","typ":"intelibill-offline-permit+v1","kid":"x"}';
    const header = Buffer.from(raw).toString('base64url');
    const compact = (head: string, body: string) => {
      const input = head + '.' + body;
      return input + '.' + sign('sha256', Buffer.from(input), pair.privateKey).toString('base64url');
    };
    expect(await verifyOfflinePermitSignature(compact(header, payload), keys)).not.toBeNull();
    for (const token of [
      compact(header.slice(0, -1) + '1', payload),
      compact(header, payload.slice(0, -1) + 'R'),
    ]) {
      expect(decodeOfflineLicencePermitToken(token)).toBeNull();
      expect(await verifyOfflinePermitSignature(token, keys)).toBeNull();
    }
  });

  for (const vector of fixture.negative) {
    it(`rejects ${vector.name} (${vector.error})`, async () => {
      expect(await verifyOfflinePermitSignature(vector.token, pins)).toBeNull();
    });
  }

  it('trusts nothing without a pin: empty map, another key id, an inherited property', async () => {
    expect(await verifyOfflinePermitSignature(fixture.permit.token, {})).toBeNull();
    expect(await verifyOfflinePermitSignature(fixture.permit.token, { 'other-key': fixture.key.publicKeySpki })).toBeNull();
    const inherited = Object.create({ [fixture.key.keyId]: fixture.key.publicKeySpki }) as Record<string, string>;
    expect(await verifyOfflinePermitSignature(fixture.permit.token, inherited)).toBeNull();
  });

  it('selects the key by the permit header, so overlapping pins verify during rotation', async () => {
    const next = await createPermitSigner('rotated-key-2');
    const fields = permitFields(Date.now());
    const token = await next.sign(fields);
    const overlapping = { ...pins, [next.keyId]: next.publicKeySpki };

    expect((await verifyOfflinePermitSignature(token, overlapping))?.keyId).toBe('rotated-key-2');
    expect((await verifyOfflinePermitSignature(fixture.permit.token, overlapping))?.keyId).toBe(fixture.key.keyId);
    // The same bytes under the other pinned key do not verify.
    expect(await verifyOfflinePermitSignature(token, { 'rotated-key-2': fixture.key.publicKeySpki })).toBeNull();
  });

  it('rejects a genuine signature from a key shorter than 2048 bits', async () => {
    const weak = await createPermitSigner('weak-key-1', 1024);
    const token = await weak.sign(permitFields(Date.now()));

    expect(await verifyOfflinePermitSignature(token, { [weak.keyId]: weak.publicKeySpki })).toBeNull();
  });

  it('rejects an unusable pin and keeps only well-formed build pins', async () => {
    expect(await verifyOfflinePermitSignature(fixture.permit.token, { [fixture.key.keyId]: 'AAAA' })).toBeNull();
    expect(readOfflinePermitPublicKeys(null)).toEqual({});
    expect(readOfflinePermitPublicKeys(['x'])).toEqual({});
    expect(readOfflinePermitPublicKeys({ good: 'QUJD', 'bad id!': 'QUJD', n: 5, alsoBad: 'not base64 ***' })).toEqual({ good: 'QUJD' });
  });
});
