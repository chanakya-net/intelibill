import type { OfflinePermitDisplaySummary } from '../../../core/storage/offline-sales-device-settings.storage';
import type { InvoiceLeaseLicenceEvidenceDto } from '../services/sale.models';

export const OFFLINE_PERMIT_VERSION = 1;
export const OFFLINE_PERMIT_TYPE = 'intelibill-offline-permit+v1';
export const OFFLINE_PERMIT_MAX_TOKEN_LENGTH = 8192;

const BASE64URL_SEGMENT = /^[A-Za-z0-9_-]+$/;
const KEY_ID_PATTERN = /^[A-Za-z0-9_-]{1,128}$/;
const SHA256_HEX = /^[0-9a-f]{64}$/;
const CANONICAL_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}Z$/;
const ISO_INSTANT = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,9}))?(Z|[+-]\d{2}:\d{2})$/;

export interface OfflineLicencePermitBinding {
  readonly shopId: string;
  readonly userId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly leaseId: string;
  readonly leaseExpiresAt: string;
  readonly grantId: string;
  readonly grantSha256: string;
  readonly tillSessionId?: string | null;
}

export type OfflineSnapshotLicencePermitRecord =
  | {
      readonly version: number;
      readonly status: 'issued';
      readonly leaseId: string;
      readonly grantId: string;
      readonly snapshotId: string;
      readonly permitId: string;
      readonly keyId: string;
      readonly signedPermit: string;
      readonly notBefore: string;
      readonly validUntil: string;
      readonly limitingReason: string;
    }
  | {
      readonly version: number;
      readonly status: 'refused';
      readonly leaseId: string;
      readonly grantId: string;
      readonly snapshotId: string;
      readonly refusalCode: string;
    };

export interface OfflineLicensePermitPayload {
  readonly v: 1;
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

export interface StoredOfflineLicencePermit {
  readonly version: 1;
  readonly permitId: string;
  readonly grantId: string;
  readonly grantSha256: string;
  readonly leaseId: string;
  readonly snapshotId: string;
  readonly keyId: string;
  readonly signedPermit: string;
  readonly shopId: string;
  readonly userId: string;
  readonly sessionId: string;
  readonly deviceId: string;
  readonly authorityVerifiedAt: string;
  readonly deviceVerifiedAt: string;
  readonly snapshotCompletedAt: string;
  readonly issuedAt: string;
  readonly notBefore: string;
  readonly validUntil: string;
  readonly limitingReason: string;
  readonly tillSessionId: string | null;
}

export interface OfflineLicencePermitExpected {
  readonly binding: OfflineLicencePermitBinding;
  readonly snapshotId: string;
  readonly snapshotCompletedAt: string;
  readonly sessionId?: string | null;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isNonNegativeInt(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

/** Parses an ISO-8601 instant with offset to UTC nanoseconds so sub-millisecond instants compare exactly. */
export function parseInstantNanos(value: unknown): bigint | null {
  if (typeof value !== 'string') return null;
  const match = ISO_INSTANT.exec(value);
  if (!match) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  if (hour > 23 || minute > 59 || second > 59) return null;

  const baseMs = Date.UTC(year, month - 1, day, hour, minute, second);
  const check = new Date(baseMs);
  if (
    !Number.isFinite(baseMs) ||
    check.getUTCFullYear() !== year ||
    check.getUTCMonth() !== month - 1 ||
    check.getUTCDate() !== day
  ) {
    return null;
  }

  let offsetMinutes = 0;
  const offset = match[8];
  if (offset !== 'Z') {
    const sign = offset[0] === '+' ? 1 : -1;
    const offHours = Number(offset.slice(1, 3));
    const offMins = Number(offset.slice(4, 6));
    if (offHours > 23 || offMins > 59) return null;
    offsetMinutes = sign * (offHours * 60 + offMins);
  }

  const utcSeconds = BigInt(baseMs / 1000) - BigInt(offsetMinutes * 60);
  const fracNanos = BigInt((match[7] ?? '').padEnd(9, '0'));
  return utcSeconds * 1_000_000_000n + fracNanos;
}

/** Compact permit segments must have a single, unpadded base64url representation. */
export function decodePermitSegment(segment: string): Uint8Array<ArrayBuffer> | null {
  if (!BASE64URL_SEGMENT.test(segment) || segment.length % 4 === 1) return null;
  try {
    const binary = atob(segment.replace(/-/g, '+').replace(/_/g, '/'));
    const canonical = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    if (canonical !== segment) return null;
    return Uint8Array.from(binary, char => char.charCodeAt(0));
  } catch {
    return null;
  }
}

function decodeBase64UrlToUtf8(segment: string): string | null {
  const bytes = decodePermitSegment(segment);
  if (!bytes) return null;
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return null;
  }
}

function readCanonicalHeaderKeyId(segment: string): string | null {
  const raw = decodeBase64UrlToUtf8(segment);
  if (!raw) return null;
  const prefix = `{"alg":"RS256","typ":"${OFFLINE_PERMIT_TYPE}","kid":"`;
  const suffix = '"}';
  if (!raw.startsWith(prefix) || !raw.endsWith(suffix)) return null;
  const keyId = raw.slice(prefix.length, raw.length - suffix.length);
  return KEY_ID_PATTERN.test(keyId) ? keyId : null;
}

function parsePayload(raw: unknown): OfflineLicensePermitPayload | null {
  if (!isObject(raw) || raw['v'] !== OFFLINE_PERMIT_VERSION) return null;
  const strings = ['permitId', 'grantId', 'shopId', 'accountId', 'userId', 'sessionId', 'deviceId', 'leaseId', 'snapshotId', 'limitingReason'] as const;
  if (strings.some((key) => !isNonEmptyString(raw[key]))) return null;
  if (typeof raw['grantSha256'] !== 'string' || !SHA256_HEX.test(raw['grantSha256'])) return null;

  const revisions = ['accountRevision', 'shopRevision', 'currentAccountRevision', 'currentShopRevision'] as const;
  if (revisions.some((key) => !isNonNegativeInt(raw[key]))) return null;

  const instants = ['authorityVerifiedAt', 'deviceVerifiedAt', 'snapshotCompletedAt', 'issuedAt', 'notBefore', 'validUntil'] as const;
  for (const key of instants) {
    const value = raw[key];
    if (typeof value !== 'string' || !CANONICAL_INSTANT.test(value) || parseInstantNanos(value) === null) return null;
  }

  const till = raw['tillSessionId'];
  if (till !== null && !isNonEmptyString(till)) return null;
  if (raw['issuedAt'] !== raw['notBefore']) return null;
  if (parseInstantNanos(raw['notBefore'])! >= parseInstantNanos(raw['validUntil'])!) return null;

  return {
    v: 1,
    permitId: raw['permitId'] as string,
    grantId: raw['grantId'] as string,
    grantSha256: raw['grantSha256'] as string,
    shopId: raw['shopId'] as string,
    accountId: raw['accountId'] as string,
    userId: raw['userId'] as string,
    sessionId: raw['sessionId'] as string,
    deviceId: raw['deviceId'] as string,
    leaseId: raw['leaseId'] as string,
    snapshotId: raw['snapshotId'] as string,
    accountRevision: raw['accountRevision'] as number,
    shopRevision: raw['shopRevision'] as number,
    currentAccountRevision: raw['currentAccountRevision'] as number,
    currentShopRevision: raw['currentShopRevision'] as number,
    authorityVerifiedAt: raw['authorityVerifiedAt'] as string,
    deviceVerifiedAt: raw['deviceVerifiedAt'] as string,
    snapshotCompletedAt: raw['snapshotCompletedAt'] as string,
    issuedAt: raw['issuedAt'] as string,
    notBefore: raw['notBefore'] as string,
    validUntil: raw['validUntil'] as string,
    limitingReason: raw['limitingReason'] as string,
    tillSessionId: till as string | null,
  };
}

export function isValidLeaseLicenceEvidence(value: unknown): value is InvoiceLeaseLicenceEvidenceDto {
  return (
    isObject(value) &&
    value['version'] === OFFLINE_PERMIT_VERSION &&
    isNonEmptyString(value['grantId']) &&
    typeof value['grantSha256'] === 'string' &&
    SHA256_HEX.test(value['grantSha256']) &&
    isNonEmptyString(value['keyId'])
  );
}

/**
 * Structural decode for delivery. Offline issuance additionally verifies the signature against its build pins.
 */
export function decodeOfflineLicencePermitToken(
  token: string,
): { readonly keyId: string; readonly payload: OfflineLicensePermitPayload } | null {
  if (typeof token !== 'string' || token.length === 0 || token.length > OFFLINE_PERMIT_MAX_TOKEN_LENGTH) return null;
  const segments = token.split('.');
  if (segments.length !== 3 || !BASE64URL_SEGMENT.test(segments[2])) return null;

  const keyId = readCanonicalHeaderKeyId(segments[0]);
  if (!keyId) return null;

  const payloadText = decodeBase64UrlToUtf8(segments[1]);
  if (!payloadText) return null;
  try {
    const payload = parsePayload(JSON.parse(payloadText));
    return payload ? { keyId, payload } : null;
  } catch {
    return null;
  }
}

export function verifyDeliveredOfflineLicencePermit(
  record: unknown,
  expected: OfflineLicencePermitExpected,
): StoredOfflineLicencePermit | null {
  if (!isObject(record) || record['version'] !== OFFLINE_PERMIT_VERSION || record['status'] !== 'issued') return null;
  if (typeof record['signedPermit'] !== 'string') return null;

  const decoded = decodeOfflineLicencePermitToken(record['signedPermit']);
  if (!decoded) return null;
  const { keyId, payload } = decoded;
  const { binding } = expected;

  if (
    record['keyId'] !== keyId ||
    record['permitId'] !== payload.permitId ||
    record['leaseId'] !== payload.leaseId ||
    record['grantId'] !== payload.grantId ||
    record['snapshotId'] !== payload.snapshotId ||
    record['limitingReason'] !== payload.limitingReason
  ) {
    return null;
  }

  if (
    payload.shopId !== binding.shopId ||
    payload.userId !== binding.userId ||
    payload.deviceId !== binding.deviceId ||
    payload.leaseId !== binding.leaseId ||
    payload.grantId !== binding.grantId ||
    payload.grantSha256 !== binding.grantSha256 ||
    payload.snapshotId !== expected.snapshotId
  ) {
    return null;
  }

  if (isNonEmptyString(expected.sessionId) && payload.sessionId !== expected.sessionId) return null;
  const expectedTill = isNonEmptyString(binding.tillSessionId) ? binding.tillSessionId : null;
  if (payload.tillSessionId !== expectedTill) return null;

  const notBefore = parseInstantNanos(payload.notBefore);
  const validUntil = parseInstantNanos(payload.validUntil);
  const recordNotBefore = parseInstantNanos(record['notBefore']);
  const recordValidUntil = parseInstantNanos(record['validUntil']);
  const payloadCompleted = parseInstantNanos(payload.snapshotCompletedAt);
  const expectedCompleted = parseInstantNanos(expected.snapshotCompletedAt);
  const leaseExpires = parseInstantNanos(binding.leaseExpiresAt);

  if (
    notBefore === null ||
    validUntil === null ||
    recordNotBefore !== notBefore ||
    recordValidUntil !== validUntil ||
    payloadCompleted === null ||
    expectedCompleted !== payloadCompleted ||
    leaseExpires === null ||
    notBefore >= validUntil ||
    validUntil > leaseExpires
  ) {
    return null;
  }

  return {
    version: 1,
    permitId: payload.permitId,
    grantId: payload.grantId,
    grantSha256: payload.grantSha256,
    leaseId: payload.leaseId,
    snapshotId: payload.snapshotId,
    keyId,
    signedPermit: record['signedPermit'],
    shopId: payload.shopId,
    userId: payload.userId,
    sessionId: payload.sessionId,
    deviceId: payload.deviceId,
    authorityVerifiedAt: payload.authorityVerifiedAt,
    deviceVerifiedAt: payload.deviceVerifiedAt,
    snapshotCompletedAt: payload.snapshotCompletedAt,
    issuedAt: payload.issuedAt,
    notBefore: payload.notBefore,
    validUntil: payload.validUntil,
    limitingReason: payload.limitingReason,
    tillSessionId: payload.tillSessionId,
  };
}

export function toOfflinePermitDisplaySummary(permit: StoredOfflineLicencePermit): OfflinePermitDisplaySummary {
  return {
    leaseId: permit.leaseId,
    grantId: permit.grantId,
    permitId: permit.permitId,
    snapshotId: permit.snapshotId,
    issuedAt: permit.issuedAt,
    notBefore: permit.notBefore,
    validUntil: permit.validUntil,
    limitingReason: permit.limitingReason,
  };
}
