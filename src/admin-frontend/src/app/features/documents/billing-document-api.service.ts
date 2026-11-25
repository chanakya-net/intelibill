import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';
import { ACCOUNTING_INVOICE, MAX_PDF_BYTES, PDF_MEDIA_TYPE, type InvoiceIntent, type InvoiceMetadata } from './billing-document-file';

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
  readonly version: 1;
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
  return object(raw) && scoped(raw, scope) && raw['version'] === 1 && raw['supersedesVersionId'] === null
    && raw['kind'] === ACCOUNTING_INVOICE && raw['mediaType'] === PDF_MEDIA_TYPE
    && ['documentId', 'versionId', 'operationKey', 'uploadedBy'].every(key => typeof raw[key] === 'string' && uuid.test(raw[key]))
    && ['fileName', 'issuerName', 'issuerReference', 'issuedOn', 'uploadedAt'].every(key => text(raw[key]))
    && text(raw['sha256']) && digest.test(raw['sha256']) && integer(raw['byteLength'], 1) && raw['byteLength'] <= MAX_PDF_BYTES;
}

@Injectable({ providedIn: 'root' })
export class BillingDocumentApi {
  private readonly http = inject(HttpClient);

  async list(scope: InvoiceScope): Promise<InvoiceList> {
    const raw = await this.get(scope, '');
    if (!orderView(raw, scope) || !Array.isArray(raw['documents']) || !raw['documents'].every(row => version(row, scope)))
      throw new Error('Invalid invoice list');
    return { ...raw, documents: raw['documents'] };
  }

  async preview(scope: InvoiceScope, metadata: InvoiceMetadata): Promise<InvoicePreview> {
    const raw = await this.post(scope, '/preview', metadata);
    if (!orderView(raw, scope) || raw['version'] !== 1 || !object(raw['metadata']) ||
      !sameMetadata(raw['metadata'] as unknown as InvoiceMetadata, metadata) ||
      !text(raw['previewDigest']) || !digest.test(raw['previewDigest'])) throw new Error('Invalid invoice preview');
    return { ...raw, version: 1, metadata, previewDigest: raw['previewDigest'] };
  }

  async finish(scope: InvoiceScope, intent: InvoiceIntent, lookup: boolean): Promise<InvoiceVersion> {
    const raw = lookup ? await this.get(scope, `/operations/${encodeURIComponent(intent.operationKey)}`)
      : await this.post(scope, '', this.form(intent));
    if (!version(raw, scope) || raw.operationKey !== intent.operationKey || raw.uploadedBy !== scope.admin
      || !sameMetadata(raw, intent.metadata)) throw new Error('Invalid upload receipt');
    return raw;
  }

  private form(intent: InvoiceIntent) {
    const { metadata, operationKey, previewDigest, bytes } = intent;
    const form = new FormData();
    form.append('command', new Blob([JSON.stringify({ ...metadata, operationKey, previewDigest, confirmed: true })],
      { type: 'application/json' }), 'command.json');
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
