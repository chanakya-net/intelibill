import { InjectionToken } from '@angular/core';

import {
  OFFLINE_PERMIT_MAX_TOKEN_LENGTH,
  decodeOfflineLicencePermitToken,
  decodePermitSegment,
  type OfflineLicensePermitPayload,
} from './offline-licence-permit';

/**
 * Replaced at build time (Angular `define`) with the release's approved `kid -> base64 DER SPKI` map. The default is
 * an empty map: a build without pins issues nothing offline. Only public halves ever belong here.
 */
declare const IB_OFFLINE_PERMIT_PUBLIC_KEYS: unknown;

export type OfflinePermitPublicKeys = Readonly<Record<string, string>>;

export const OFFLINE_PERMIT_MIN_MODULUS_BITS = 2048;

const KEY_ID_PATTERN = /^[A-Za-z0-9_-]{1,128}$/;
const BASE64_PATTERN = /^[A-Za-z0-9+/]+={0,2}$/;
const LOWER_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const GUID_MEMBERS = ['permitId', 'grantId', 'shopId', 'accountId', 'userId', 'sessionId', 'leaseId', 'snapshotId'] as const;

/** Keeps only well-formed own entries, so a malformed build value can never widen trust. */
export function readOfflinePermitPublicKeys(value: unknown): OfflinePermitPublicKeys {
  const pins: Record<string, string> = Object.create(null) as Record<string, string>;
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return pins;
  for (const [keyId, spki] of Object.entries(value)) {
    if (KEY_ID_PATTERN.test(keyId) && typeof spki === 'string' && BASE64_PATTERN.test(spki)) pins[keyId] = spki;
  }
  return pins;
}

function readBuildPins(): OfflinePermitPublicKeys {
  return readOfflinePermitPublicKeys(
    typeof IB_OFFLINE_PERMIT_PUBLIC_KEYS === 'undefined' ? null : IB_OFFLINE_PERMIT_PUBLIC_KEYS,
  );
}

/** The public keys this build trusts for offline permits. Tests provide their own throwaway public half. */
export const OFFLINE_PERMIT_PUBLIC_KEYS = new InjectionToken<OfflinePermitPublicKeys>('OFFLINE_PERMIT_PUBLIC_KEYS', {
  providedIn: 'root',
  factory: readBuildPins,
});

export interface VerifiedOfflinePermit {
  readonly keyId: string;
  readonly signedPermit: string;
  readonly payload: OfflineLicensePermitPayload;
}

/** The exact member order and spelling the merchant API signs. */
function canonicalPayloadJson(payload: OfflineLicensePermitPayload): string {
  return JSON.stringify({
    v: payload.v,
    permitId: payload.permitId,
    grantId: payload.grantId,
    grantSha256: payload.grantSha256,
    shopId: payload.shopId,
    accountId: payload.accountId,
    userId: payload.userId,
    sessionId: payload.sessionId,
    deviceId: payload.deviceId,
    leaseId: payload.leaseId,
    snapshotId: payload.snapshotId,
    accountRevision: payload.accountRevision,
    shopRevision: payload.shopRevision,
    currentAccountRevision: payload.currentAccountRevision,
    currentShopRevision: payload.currentShopRevision,
    authorityVerifiedAt: payload.authorityVerifiedAt,
    deviceVerifiedAt: payload.deviceVerifiedAt,
    snapshotCompletedAt: payload.snapshotCompletedAt,
    issuedAt: payload.issuedAt,
    notBefore: payload.notBefore,
    validUntil: payload.validUntil,
    limitingReason: payload.limitingReason,
    tillSessionId: payload.tillSessionId,
  });
}

function decodeBase64(value: string): Uint8Array<ArrayBuffer> | null {
  try {
    const binary = atob(value);
    const bytes = new Uint8Array(new ArrayBuffer(binary.length));
    for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
    return bytes;
  } catch {
    return null;
  }
}

function isCanonical(payloadSegment: string, payload: OfflineLicensePermitPayload): boolean {
  const bytes = decodePermitSegment(payloadSegment);
  if (!bytes) return false;
  let text: string;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return false;
  }
  if (text !== canonicalPayloadJson(payload)) return false;
  if (GUID_MEMBERS.some((member) => !LOWER_GUID.test(payload[member]))) return false;
  return payload.tillSessionId === null || LOWER_GUID.test(payload.tillSessionId);
}

const importedKeys = new Map<string, Promise<CryptoKey | null>>();

async function importPin(spki: string): Promise<CryptoKey | null> {
  const der = decodeBase64(spki);
  if (!der || typeof crypto === 'undefined' || !crypto.subtle) return null;
  try {
    const key = await crypto.subtle.importKey('spki', der, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']);
    const bits = (key.algorithm as RsaHashedKeyAlgorithm).modulusLength;
    return bits >= OFFLINE_PERMIT_MIN_MODULUS_BITS ? key : null;
  } catch {
    return null;
  }
}

function pinnedKey(keys: OfflinePermitPublicKeys, keyId: string): Promise<CryptoKey | null> {
  if (!Object.prototype.hasOwnProperty.call(keys, keyId)) return Promise.resolve(null);
  const spki = keys[keyId];
  const cacheKey = `${keyId}\n${spki}`;
  let imported = importedKeys.get(cacheKey);
  if (!imported) {
    imported = importPin(spki);
    importedKeys.set(cacheKey, imported);
  }
  return imported;
}

/**
 * Proves a compact permit was signed by a key this build pins: exact permit header, canonical payload bytes, and an
 * RS256 signature over the original signing input. Answers null for anything else, including a missing pin.
 */
export async function verifyOfflinePermitSignature(
  token: unknown,
  keys: OfflinePermitPublicKeys,
): Promise<VerifiedOfflinePermit | null> {
  if (typeof token !== 'string' || token.length === 0 || token.length > OFFLINE_PERMIT_MAX_TOKEN_LENGTH) return null;
  const decoded = decodeOfflineLicencePermitToken(token);
  if (!decoded) return null;

  const [header, payload, signature] = token.split('.');
  if (!isCanonical(payload, decoded.payload)) return null;

  const key = await pinnedKey(keys, decoded.keyId);
  const signatureBytes = decodePermitSegment(signature);
  if (!key || !signatureBytes) return null;

  try {
    const signingInput = new TextEncoder().encode(`${header}.${payload}`);
    const valid = await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, signatureBytes, signingInput);
    return valid ? { keyId: decoded.keyId, signedPermit: token, payload: decoded.payload } : null;
  } catch {
    return null;
  }
}
