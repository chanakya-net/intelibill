import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { createHash } from 'node:crypto';
import { BehaviorSubject } from 'rxjs';
import { expect, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { installStorage } from '../refunds/refund-approval.test-utils';
import { BillingDocumentPageComponent } from './billing-document-page.component';

export const account = '11111111-1111-4111-8111-111111111111';
export const order = '22222222-2222-4222-8222-222222222222';
export const admin = '33333333-3333-4333-8333-333333333333';
export const other = '44444444-4444-4444-8444-444444444444';
export const session = '55555555-5555-4555-8555-555555555555';
export const v2VersionId = '66666666-6666-4666-8666-666666666666';
export const v3VersionId = '77777777-7777-4777-8777-777777777777';
export const path = `/api/admin/accounts/${account}/orders/${order}/billing-documents`;
export const draftKey = [admin, account, order].join('.');
export const bytes = new TextEncoder().encode('%PDF-1.7\nClient-side header check only\n');
export const sha256 = createHash('sha256').update(bytes).digest('hex');
export const v2Bytes = new TextEncoder().encode('%PDF-1.7\nCorrected accounting invoice v2\n');
export const v2Sha256 = createHash('sha256').update(v2Bytes).digest('hex');
export const v3Bytes = new TextEncoder().encode('%PDF-1.7\nCorrected accounting invoice v3\n');
export const v3Sha256 = createHash('sha256').update(v3Bytes).digest('hex');
export const metadata = {
  kind: 'AccountingInvoice' as const, fileName: 'fixture.pdf', mediaType: 'application/pdf' as const,
  byteLength: bytes.length, sha256, issuerName: 'Fixture Accounting', issuerReference: 'INV-114', issuedOn: '2027-03-10',
};
export const v2Metadata = {
  ...metadata, fileName: 'fixture-v2.pdf', byteLength: v2Bytes.length, sha256: v2Sha256, issuerReference: 'INV-114-CORR-1',
};
export const v3Metadata = {
  ...metadata, fileName: 'fixture-v3.pdf', byteLength: v3Bytes.length, sha256: v3Sha256, issuerReference: 'INV-114-CORR-2',
};

export const view = (documents: unknown[] = []) => ({
  schemaVersion: 1, accountId: account, orderId: order, paymentConfirmations: 1, documents,
  order: {
    planName: 'Fixture', period: 'Month', amountPaise: 12000, currency: 'INR', revision: 1,
    snapshotHash: 'a'.repeat(64), acceptedAt: '2027-03-10T01:00:00Z',
  },
});

export const savedVersion = (operationKey: string, overrides = {}) => ({
  schemaVersion: 1, ...metadata, accountId: account, orderId: order, operationKey,
  version: 1, documentId: order, versionId: other, supersedesVersionId: null, supersessionReason: null,
  uploadedBy: admin, uploadedAt: '2027-03-10T02:00:00Z', ...overrides,
});

export async function open(capabilities = ['billing'], initialDocs: unknown[] = [], uuids = [session]) {
  const records = installStorage();
  const subtle = {
    digest: async (_algorithm: string, input: ArrayBuffer) => Uint8Array.from(createHash('sha256')
      .update(new Uint8Array(input)).digest()).buffer,
  };
  let nextUuid = 0;
  vi.stubGlobal('crypto', { subtle, randomUUID: () => uuids[Math.min(nextUuid++, uuids.length - 1)] });
  const params = new BehaviorSubject(convertToParamMap({ account, order }));
  const sessionId = signal(session);
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap: params } },
      { provide: AdminSessionService, useValue: { adminId: signal(admin), sessionId, capabilities: signal(capabilities) } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const makeFixture = () => TestBed.createComponent(BillingDocumentPageComponent);
  let fixture = makeFixture();
  const page = () => fixture.nativeElement as HTMLElement;
  const sync = () => fixture.detectChanges();
  const take = (method: string, url: string) => vi.waitFor(() => {
    sync();
    const [request] = http.match(request => request.url === url && request.method === method);
    expect(request, `${method} ${url} (alert: ${page().querySelector('[role=alert]')?.textContent?.trim() ?? 'none'})`).toBeDefined();
    return request;
  });
  const waitForText = (text: string) => vi.waitFor(() => { sync(); expect(page().textContent).toContain(text); });
  const load = async (docs = initialDocs) => {
    (await take('GET', path)).flush(view(docs));
    await vi.waitFor(() => { sync(); expect(page().querySelector('#order-heading')).not.toBeNull(); });
  };
  sync();
  if (capabilities.includes('billing')) await load();
  return {
    records, http, page, take, sync, waitForText, params, sessionId, load,
    reload: () => { fixture.destroy(); fixture = makeFixture(); sync(); },
  };
}

export type Harness = Awaited<ReturnType<typeof open>>;

export function review(h: Harness, fileBytes = bytes, name = metadata.fileName, size = fileBytes.length) {
  const page = h.page();
  const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
  const read = vi.fn(async () => Uint8Array.from(fileBytes).buffer);
  Object.defineProperty(input, 'files', {
    configurable: true,
    value: { item: () => ({ name, type: 'application/pdf', size, arrayBuffer: read }) },
  });
  for (const key of ['issuerName', 'issuerReference', 'issuedOn'] as const) {
    page.querySelector<HTMLInputElement>(`[name=${key}]`)!.value = metadata[key];
  }
  input.form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  return read;
}

export function submitCorrection(h: Harness, meta: typeof v2Metadata, fileBytes: Uint8Array, reason: string) {
  const page = h.page();
  const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
  const read = vi.fn(async () => Uint8Array.from(fileBytes).buffer);
  Object.defineProperty(input, 'files', {
    configurable: true,
    value: { item: () => ({ name: meta.fileName, type: 'application/pdf', size: fileBytes.length, arrayBuffer: read }) },
  });
  page.querySelector<HTMLInputElement>('[name=issuerName]')!.value = meta.issuerName;
  page.querySelector<HTMLInputElement>('[name=issuerReference]')!.value = meta.issuerReference;
  page.querySelector<HTMLInputElement>('[name=issuedOn]')!.value = meta.issuedOn;
  page.querySelector<HTMLTextAreaElement>('[name=reason]')!.value = reason;
  input.form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
}

export async function post(h: Harness, suffix: string) {
  (await h.take('GET', '/api/admin/auth/sign-in/context')).flush({ requestToken: 'fixture-csrf' });
  const upload = await h.take('POST', path + suffix);
  expect(upload.request.headers.get('X-Admin-Session')).toBe(session);
  expect(upload.request.headers.get('X-Admin-CSRF')).toBe('fixture-csrf');
  return upload;
}

export async function confirm(h: Harness) {
  review(h);
  const request = await post(h, '/preview');
  expect(request.request.body).toEqual(metadata);
  request.flush({ ...view(), metadata, version: 1, previewDigest: 'b'.repeat(64) });
  await h.waitForText('Invoice metadata preview');
  expect(h.page().querySelectorAll('iframe, embed, object')).toHaveLength(0);
  click(h, 'Attach accounting invoice', false);
  h.page().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
  h.sync();
  click(h, 'Attach accounting invoice');
  return post(h, '');
}

export async function openCorrectionPreview(
  h: Harness, predecessorId: string, predecessorVersion: number, docs: unknown[],
  meta: typeof v2Metadata, fileBytes: Uint8Array, reason: string, previewDigest: string,
) {
  click(h, 'Supersede current invoice');
  h.sync();
  submitCorrection(h, meta, fileBytes, reason);
  const previewReq = await post(h, `/${predecessorId}/supersede/preview`);
  expect(previewReq.request.body).toEqual({ ...meta, reason });
  previewReq.flush({
    ...view(docs), documentId: order, supersedesVersionId: predecessorId,
    supersedesVersion: predecessorVersion, version: predecessorVersion + 1,
    metadata: meta, reason, previewDigest,
  });
  await h.waitForText('Correction preview');
}

export async function confirmCorrectionUpload(h: Harness, predecessorId: string) {
  click(h, 'Attach corrective invoice', false);
  h.page().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
  h.sync();
  click(h, 'Attach corrective invoice');
  return post(h, `/${predecessorId}/supersede`);
}

export function badges(h: Harness) {
  return [...h.page().querySelectorAll('.adm-version-badge')].map(el => el.textContent?.trim());
}

export function click(h: Harness, label: string, enabled = true) {
  const button = [...h.page().querySelectorAll('button')].find(candidate => candidate.textContent?.trim() === label);
  expect(button, `button "${label}"`).toBeDefined();
  expect(button!.disabled).toBe(!enabled);
  if (enabled) button!.click();
}
