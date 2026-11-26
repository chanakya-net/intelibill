export const BILLING_DOCUMENT_SCHEMA_VERSION = 1;
export const BILLING_DOCUMENT_KIND = 'AccountingInvoice' as const;
export const BILLING_DOCUMENT_MEDIA_TYPE = 'application/pdf' as const;
export const MAX_BILLING_DOCUMENT_BYTES = 10 * 1024 * 1024;
export const BILLING_DOCUMENT_PAGE_SIZE = 25;
export const MAX_ISSUER_NAME_LENGTH = 200;
export const MAX_ISSUER_REFERENCE_LENGTH = 64;
export const EARLIEST_ISSUE_DATE = '2000-01-01';
export const BILLING_DOCUMENT_FILENAME_PREFIX = 'accounting-invoice-';
export const BILLING_DOCUMENT_FILENAME_SUFFIX = '.pdf';

export interface BillingDocument {
  readonly accountId: string;
  readonly orderId: string;
  readonly documentId: string;
  readonly versionId: string;
  readonly version: number;
  readonly supersedesVersionId: string | null;
  readonly kind: typeof BILLING_DOCUMENT_KIND;
  readonly mediaType: typeof BILLING_DOCUMENT_MEDIA_TYPE;
  readonly byteLength: number;
  readonly sha256: string;
  readonly issuerName: string;
  readonly issuerReference: string;
  readonly issuedOn: string;
  readonly uploadedAt: string;
}

export interface BillingDocumentList {
  readonly schemaVersion: number;
  readonly accountId: string;
  readonly orderId: string;
  readonly documents: readonly BillingDocument[];
}

const UUID_PATTERN = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const ZERO_UUID = '00000000-0000-0000-0000-000000000000';
const SHA256_PATTERN = /^[0-9a-f]{64}$/;
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function isNonZeroUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_PATTERN.test(value) && value.toLowerCase() !== ZERO_UUID;
}

function isIssuerText(value: unknown, maxLength: number): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxLength || value !== value.trim()) {
    return false;
  }
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (code < 0x20 || code === 0x7f) return false;
  }
  return true;
}

function isValidIssueDate(value: unknown): value is string {
  if (typeof value !== 'string' || !DATE_PATTERN.test(value) || value < EARLIEST_ISSUE_DATE) {
    return false;
  }
  const [year, month, day] = value.split('-').map(Number);
  const parsed = new Date(Date.UTC(year, month - 1, day));
  return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 && parsed.getUTCDate() === day;
}

function isValidDocument(row: unknown, account: string, order: string): row is BillingDocument {
  if (!row || typeof row !== 'object') return false;
  const r = row as Record<string, unknown>;
  const version = r['version'];
  const supersedes = r['supersedesVersionId'];
  const lineageValid =
    typeof version === 'number' &&
    Number.isSafeInteger(version) &&
    version >= 1 &&
    (version === 1
      ? supersedes === null
      : isNonZeroUuid(supersedes) && supersedes !== r['versionId']);

  return (
    r['accountId'] === account &&
    r['orderId'] === order &&
    isNonZeroUuid(r['documentId']) &&
    isNonZeroUuid(r['versionId']) &&
    lineageValid &&
    r['kind'] === BILLING_DOCUMENT_KIND &&
    r['mediaType'] === BILLING_DOCUMENT_MEDIA_TYPE &&
    typeof r['byteLength'] === 'number' &&
    Number.isSafeInteger(r['byteLength']) &&
    r['byteLength'] > 0 &&
    r['byteLength'] <= MAX_BILLING_DOCUMENT_BYTES &&
    typeof r['sha256'] === 'string' &&
    SHA256_PATTERN.test(r['sha256']) &&
    isIssuerText(r['issuerName'], MAX_ISSUER_NAME_LENGTH) &&
    isIssuerText(r['issuerReference'], MAX_ISSUER_REFERENCE_LENGTH) &&
    isValidIssueDate(r['issuedOn']) &&
    typeof r['uploadedAt'] === 'string' &&
    Number.isFinite(Date.parse(r['uploadedAt']))
  );
}

export function billingDocumentFileName(versionId: string): string {
  if (!isNonZeroUuid(versionId)) {
    throw new Error('Invalid billing document version identifier');
  }
  return `${BILLING_DOCUMENT_FILENAME_PREFIX}${versionId.toLowerCase()}${BILLING_DOCUMENT_FILENAME_SUFFIX}`;
}

export function parseBillingDocumentList(
  value: unknown,
  account: string,
  order: string,
): readonly BillingDocument[] {
  if (!value || typeof value !== 'object') {
    throw new Error('Invalid billing documents');
  }
  const payload = value as Record<string, unknown>;
  const rows = payload['documents'];
  if (
    payload['schemaVersion'] !== BILLING_DOCUMENT_SCHEMA_VERSION ||
    payload['accountId'] !== account ||
    payload['orderId'] !== order ||
    !Array.isArray(rows) ||
    rows.length > BILLING_DOCUMENT_PAGE_SIZE ||
    !rows.every((row) => isValidDocument(row, account, order)) ||
    new Set(rows.map((row) => row.versionId)).size !== rows.length
  ) {
    throw new Error('Invalid billing documents');
  }
  return rows;
}
