/**
 * Test-only signer for `OfflineLicensePermitV1`. It makes a throwaway RSA-2048 key in the test process, signs the
 * canonical bytes the merchant API signs, and hands out only the public half (base64 DER SPKI) as a trust pin.
 * Nothing here is a production key and nothing here proves a live provider.
 */
export const PERMIT_SHOP_ID = '11111111-2222-4333-8444-555555555555';
export const PERMIT_USER_ID = '99999999-8888-4777-8666-555555555555';
export const PERMIT_SESSION_ID = 'abcdefab-cdef-4abc-8def-abcdefabcdef';
export const PERMIT_DEVICE_ID = 'device-1';
export const PERMIT_LEASE_ID = 'fedcba98-7654-4321-8fed-cba987654321';
export const PERMIT_GRANT_ID = '0a6f3c52-7d1e-4b8a-9c55-1f2e3d4c5b6a';
export const PERMIT_GRANT_SHA256 = '6eceaedcda11fb4caa3d3bc4a9894ed5ebe33141ea01c3d2beee62c58094ba81';
export const PERMIT_SNAPSHOT_ID = '5a5a5a5a-1111-4222-8333-444444444444';
export const PERMIT_KEY_ID = 'test-permit-key-1';

export interface PermitFields {
  readonly v: number;
  readonly permitId: string;
  readonly grantId: string;
  readonly grantSha256: string;
  readonly shopId: string;
  readonly accountId: string;
  readonly userId: string;
  readonly sessionId: string;
  readonly deviceId: string;
  readonly leaseId: string;
  readonly snapshotId: string;
  readonly accountRevision: number;
  readonly shopRevision: number;
  readonly currentAccountRevision: number;
  readonly currentShopRevision: number;
  readonly authorityVerifiedAt: string;
  readonly deviceVerifiedAt: string;
  readonly snapshotCompletedAt: string;
  readonly issuedAt: string;
  readonly notBefore: string;
  readonly validUntil: string;
  readonly limitingReason: string;
  readonly tillSessionId: string | null;
}

export interface PermitSigner {
  readonly keyId: string;
  /** The public half only: base64 DER SubjectPublicKeyInfo. */
  readonly publicKeySpki: string;
  sign(fields: PermitFields, keyId?: string): Promise<string>;
}

/** A UTC instant with six fractional digits, `extraMicros` past the millisecond. */
export function canonicalInstant(epochMs: number, extraMicros = 0): string {
  const micros = String(extraMicros).padStart(3, '0');
  return new Date(epochMs).toISOString().replace('Z', `${micros}Z`);
}

export function permitFields(nowMs: number, overrides: Partial<PermitFields> = {}): PermitFields {
  const start = canonicalInstant(nowMs - 60_000);
  return {
    v: 1,
    permitId: '5e1ec7ed-0000-4000-8000-00000000abcd',
    grantId: PERMIT_GRANT_ID,
    grantSha256: PERMIT_GRANT_SHA256,
    shopId: PERMIT_SHOP_ID,
    accountId: 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee',
    userId: PERMIT_USER_ID,
    sessionId: PERMIT_SESSION_ID,
    deviceId: PERMIT_DEVICE_ID,
    leaseId: PERMIT_LEASE_ID,
    snapshotId: PERMIT_SNAPSHOT_ID,
    accountRevision: 7,
    shopRevision: 12,
    currentAccountRevision: 7,
    currentShopRevision: 12,
    authorityVerifiedAt: canonicalInstant(nowMs - 3_600_000),
    deviceVerifiedAt: canonicalInstant(nowMs - 120_000),
    snapshotCompletedAt: start,
    issuedAt: start,
    notBefore: start,
    validUntil: canonicalInstant(nowMs + 3_600_000),
    limitingReason: 'AuthorityFreshness',
    tillSessionId: null,
    ...overrides,
  };
}

const base64Url = (bytes: Uint8Array): string => {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
};

const utf8 = (value: string): Uint8Array => new TextEncoder().encode(value);

export async function createPermitSigner(keyId = PERMIT_KEY_ID, modulusLength = 2048): Promise<PermitSigner> {
  const pair = await crypto.subtle.generateKey(
    { name: 'RSASSA-PKCS1-v1_5', modulusLength, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' },
    true,
    ['sign', 'verify'],
  );
  const spki = new Uint8Array(await crypto.subtle.exportKey('spki', pair.publicKey));
  let binary = '';
  for (const byte of spki) binary += String.fromCharCode(byte);

  return {
    keyId,
    publicKeySpki: btoa(binary),
    async sign(fields, headerKeyId = keyId) {
      const header = `{"alg":"RS256","typ":"intelibill-offline-permit+v1","kid":"${headerKeyId}"}`;
      const signingInput = `${base64Url(utf8(header))}.${base64Url(utf8(JSON.stringify(fields)))}`;
      const signature = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', pair.privateKey, utf8(signingInput) as BufferSource);
      return `${signingInput}.${base64Url(new Uint8Array(signature))}`;
    },
  };
}

/** The stored permit record the delivery path keeps on the lease, built from the signed fields. */
export function storedPermit(fields: PermitFields, signedPermit: string, keyId = PERMIT_KEY_ID) {
  return {
    version: 1 as const,
    permitId: fields.permitId,
    grantId: fields.grantId,
    grantSha256: fields.grantSha256,
    leaseId: fields.leaseId,
    snapshotId: fields.snapshotId,
    keyId,
    signedPermit,
    shopId: fields.shopId,
    userId: fields.userId,
    sessionId: fields.sessionId,
    deviceId: fields.deviceId,
    authorityVerifiedAt: fields.authorityVerifiedAt,
    deviceVerifiedAt: fields.deviceVerifiedAt,
    snapshotCompletedAt: fields.snapshotCompletedAt,
    issuedAt: fields.issuedAt,
    notBefore: fields.notBefore,
    validUntil: fields.validUntil,
    limitingReason: fields.limitingReason,
    tillSessionId: fields.tillSessionId,
  };
}

/** Flips one bit of the signature so the token stays well-formed but no longer verifies. */
export function withForgedSignature(token: string): string {
  const [header, payload, signature] = token.split('.');
  const swapped = signature[0] === 'A' ? 'B' : 'A';
  return `${header}.${payload}.${swapped}${signature.slice(1)}`;
}
