import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';
import {
  ACCOUNTING_INVOICE,
  MAX_PDF_BYTES,
  PDF_MEDIA_TYPE,
  isSupersedeIntent,
  type InvoiceIntent,
  type InvoiceMetadata,
} from './billing-document-file';

export interface InvoiceScope {
  readonly admin: string;
  readonly session: string;
  readonly account: string;
  readonly order: string;
}

interface OrderSummary {
  readonly planName: string;
  readonly period: string;
  readonly amountPaise: number;
  readonly currency: string;
  readonly revision: number;
  readonly snapshotHash: string;
  readonly acceptedAt: string;
}

interface ScopedOrder {
  readonly accountId: string;
  readonly orderId: string;
  readonly order: OrderSummary;
  readonly paymentConfirmations: number;
}

export interface InvoiceVersion extends InvoiceMetadata {
  readonly documentId: string;
  readonly versionId: string;
  readonly version: number;
  readonly supersedesVersionId: string | null;
  readonly supersessionReason: string | null;
  readonly operationKey: string;
  readonly uploadedBy: string;
  readonly uploadedAt: string;
}

export interface InvoiceList extends ScopedOrder {
  readonly documents: readonly InvoiceVersion[];
}

export interface InvoicePreview extends ScopedOrder {
  readonly version: 1;
  readonly metadata: InvoiceMetadata;
  readonly previewDigest: string;
}

export interface InvoiceSupersessionPreview extends ScopedOrder {
  readonly documentId: string;
  readonly supersedesVersionId: string;
  readonly supersedesVersion: number;
  readonly version: number;
  readonly metadata: InvoiceMetadata;
  readonly reason: string;
  readonly previewDigest: string;
}

type JsonObject = Record<string, unknown>;
const digest = /^[a-f0-9]{64}$/;
const uuid = /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i;
export function invoiceScopeId(value: string | null): string {
  const id = value?.trim() ?? '';
  return uuid.test(id) && id !== '00000000-0000-0000-0000-000000000000' ? id.toLowerCase() : '';
}

function object(value: unknown): value is JsonObject {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function integer(value: unknown, min: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= min;
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && uuid.test(value);
}

function scoped(raw: JsonObject, scope: InvoiceScope) {
  return raw['schemaVersion'] === 1 && raw['accountId'] === scope.account && raw['orderId'] === scope.order;
}

function orderView(raw: unknown, scope: InvoiceScope): raw is JsonObject & ScopedOrder {
  if (!object(raw) || !scoped(raw, scope) || !integer(raw['paymentConfirmations'], 0)) return false;
  const order = raw['order'];
  return object(order) && text(order['planName']) && (order['period'] === 'Month' || order['period'] === 'Year')
    && integer(order['amountPaise'], 1) && order['currency'] === 'INR' && integer(order['revision'], 1)
    && text(order['snapshotHash']) && digest.test(order['snapshotHash']) && text(order['acceptedAt']);
}

function sameMetadata(raw: InvoiceMetadata, expected: InvoiceMetadata) {
  return raw.kind === expected.kind && raw.fileName === expected.fileName && raw.mediaType === expected.mediaType
    && raw.byteLength === expected.byteLength && raw.sha256 === expected.sha256 && raw.issuerName === expected.issuerName
    && raw.issuerReference === expected.issuerReference && raw.issuedOn === expected.issuedOn;
}

function version(raw: unknown, scope: InvoiceScope): raw is InvoiceVersion {
  if (!object(raw) || !scoped(raw, scope) || !integer(raw['version'], 1)) return false;
  const num = raw['version'];
  const supersedes = raw['supersedesVersionId'];
  const reason = raw['supersessionReason'];
  const lineageOk = num === 1
    ? supersedes === null && (reason === null || reason === undefined)
    : isUuid(supersedes) && supersedes !== raw['versionId'] && text(reason);
  return lineageOk
    && raw['kind'] === ACCOUNTING_INVOICE && raw['mediaType'] === PDF_MEDIA_TYPE
    && ['documentId', 'versionId', 'operationKey', 'uploadedBy'].every(key => isUuid(raw[key]))
    && ['fileName', 'issuerName', 'issuerReference', 'issuedOn', 'uploadedAt'].every(key => text(raw[key]))
    && text(raw['sha256']) && digest.test(raw['sha256']) && integer(raw['byteLength'], 1) && raw['byteLength'] <= MAX_PDF_BYTES;
}

function normalizeVersion(raw: InvoiceVersion): InvoiceVersion {
  return raw.supersessionReason === undefined ? { ...raw, supersessionReason: null } : raw;
}

function hasDescendingVersions(rows: readonly InvoiceVersion[], beforeVersion?: number): boolean {
  for (let i = 0; i < rows.length; i++) {
    if (beforeVersion !== undefined && rows[i].version >= beforeVersion) return false;
    if (i > 0 && rows[i].version >= rows[i - 1].version) return false;
  }
  return new Set(rows.map(r => r.versionId)).size === rows.length;
}

@Injectable({ providedIn: 'root' })
export class BillingDocumentApi {
  private readonly http = inject(HttpClient);

  async list(scope: InvoiceScope, beforeVersion?: number): Promise<InvoiceList> {
    if (beforeVersion !== undefined && (!Number.isSafeInteger(beforeVersion) || beforeVersion <= 1))
      throw new Error('Invalid invoice version cursor');
    const suffix = beforeVersion === undefined ? '' : `/before/${beforeVersion}`;
    const raw = await this.get(scope, suffix);
    if (!orderView(raw, scope) || !Array.isArray(raw['documents']) || !raw['documents'].every(row => version(row, scope)))
      throw new Error('Invalid invoice list');
    const documents = (raw['documents'] as InvoiceVersion[]).map(normalizeVersion);
    if (!hasDescendingVersions(documents, beforeVersion)) throw new Error('Invalid invoice list');
    return { ...raw, documents };
  }

  async preview(scope: InvoiceScope, metadata: InvoiceMetadata): Promise<InvoicePreview> {
    const raw = await this.post(scope, '/preview', metadata);
    if (!orderView(raw, scope) || raw['version'] !== 1 || !object(raw['metadata']) ||
      !sameMetadata(raw['metadata'] as unknown as InvoiceMetadata, metadata) ||
      !text(raw['previewDigest']) || !digest.test(raw['previewDigest'])) throw new Error('Invalid invoice preview');
    return { ...raw, version: 1, metadata, previewDigest: raw['previewDigest'] };
  }

  async previewSupersede(
    scope: InvoiceScope,
    predecessorId: string,
    metadata: InvoiceMetadata,
    reason: string,
  ): Promise<InvoiceSupersessionPreview> {
    const raw = await this.post(scope, `/${encodeURIComponent(predecessorId)}/supersede/preview`, { ...metadata, reason });
    if (
      !orderView(raw, scope) || !isUuid(raw['documentId']) || raw['supersedesVersionId'] !== predecessorId ||
      !integer(raw['supersedesVersion'], 1) || raw['version'] !== raw['supersedesVersion'] + 1 ||
      !object(raw['metadata']) || !sameMetadata(raw['metadata'] as unknown as InvoiceMetadata, metadata) ||
      raw['reason'] !== reason || !text(raw['previewDigest']) || !digest.test(raw['previewDigest'])
    ) throw new Error('Invalid correction preview');
    return {
      ...raw,
      documentId: raw['documentId'],
      supersedesVersionId: predecessorId,
      supersedesVersion: raw['supersedesVersion'],
      version: raw['version'],
      metadata,
      reason,
      previewDigest: raw['previewDigest'],
    };
  }

  async finish(scope: InvoiceScope, intent: InvoiceIntent, lookup: boolean): Promise<InvoiceVersion> {
    const supersede = isSupersedeIntent(intent);
    const raw = lookup
      ? await this.get(scope, `/operations/${encodeURIComponent(intent.operationKey)}`)
      : supersede
        ? await this.post(scope, `/${encodeURIComponent(intent.supersedesVersionId!)}/supersede`, this.form(intent, true))
        : await this.post(scope, '', this.form(intent, false));
    if (!version(raw, scope) || raw.operationKey !== intent.operationKey || raw.uploadedBy !== scope.admin
      || !sameMetadata(raw, intent.metadata)) throw new Error('Invalid upload receipt');
    const normalized = normalizeVersion(raw);
    if (supersede) {
      if (normalized.supersedesVersionId !== intent.supersedesVersionId || normalized.supersessionReason !== intent.reason
        || (intent.supersedesVersion !== undefined && normalized.version !== intent.supersedesVersion + 1))
        throw new Error('Invalid upload receipt');
    } else if (normalized.version !== 1 || normalized.supersedesVersionId !== null) {
      throw new Error('Invalid upload receipt');
    }
    return normalized;
  }

  private form(intent: InvoiceIntent, supersede: boolean) {
    const { metadata, operationKey, previewDigest, bytes, reason } = intent;
    const payload = supersede
      ? { ...metadata, reason, operationKey, previewDigest, confirmed: true }
      : { ...metadata, operationKey, previewDigest, confirmed: true };
    const form = new FormData();
    form.append('command', new Blob([JSON.stringify(payload)], { type: 'application/json' }), 'command.json');
    form.append('content', new Blob([bytes], { type: PDF_MEDIA_TYPE }), metadata.fileName);
    return form;
  }

  private get(scope: InvoiceScope, suffix: string) {
    return firstValueFrom(this.http.get<unknown>(this.path(scope) + suffix, {
      headers: { 'X-Admin-Session': scope.session },
    }));
  }

  private post(scope: InvoiceScope, suffix: string, body: unknown) {
    return firstValueFrom(this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<unknown>(this.path(scope) + suffix, body,
        { headers: { 'X-Admin-Session': scope.session, 'X-Admin-CSRF': requestToken } })),
    ));
  }

  private path(scope: InvoiceScope) {
    return `/api/admin/accounts/${scope.account}/orders/${scope.order}/billing-documents`;
  }
}
