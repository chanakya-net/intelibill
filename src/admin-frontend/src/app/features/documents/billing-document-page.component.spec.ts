import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { createHash } from 'node:crypto';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { installStorage } from '../refunds/refund-approval.test-utils';
import { MAX_PDF_BYTES, type InvoiceIntent } from './billing-document-file';
import { BillingDocumentPageComponent } from './billing-document-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const order = '22222222-2222-4222-8222-222222222222';
const admin = '33333333-3333-4333-8333-333333333333';
const other = '44444444-4444-4444-8444-444444444444';
const session = '55555555-5555-4555-8555-555555555555';
const path = `/api/admin/accounts/${account}/orders/${order}/billing-documents`;
const draftKey = [admin, account, order].join('.');
const bytes = new TextEncoder().encode('%PDF-1.7\nClient-side header check only\n');
const sha256 = createHash('sha256').update(bytes).digest('hex');
const metadata = {
  kind: 'AccountingInvoice', fileName: 'fixture.pdf', mediaType: 'application/pdf', byteLength: bytes.length, sha256,
  issuerName: 'Fixture Accounting', issuerReference: 'INV-114', issuedOn: '2027-03-10',
};
const view = {
  schemaVersion: 1, accountId: account, orderId: order, paymentConfirmations: 1, documents: [],
  order: { planName: 'Fixture', period: 'Month', amountPaise: 12000, currency: 'INR', revision: 1,
    snapshotHash: 'a'.repeat(64), acceptedAt: '2027-03-10T01:00:00Z' },
};
const savedVersion = (operationKey: string, overrides = {}) => ({
  schemaVersion: 1, ...metadata, accountId: account, orderId: order, operationKey,
  version: 1, documentId: order, versionId: other, supersedesVersionId: null, uploadedBy: admin,
  uploadedAt: '2027-03-10T02:00:00Z', ...overrides,
});

async function open(capabilities = ['billing']) {
  const records = installStorage();
  const subtle = {
    digest: async (_algorithm: string, input: ArrayBuffer) => Uint8Array.from(createHash('sha256')
      .update(new Uint8Array(input)).digest()).buffer,
  };
  vi.stubGlobal('crypto', { subtle, randomUUID: () => session });
  const params = new BehaviorSubject(convertToParamMap({ account, order }));
  const sessionId = signal(session);
  TestBed.configureTestingModule({ providers: [
    provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    { provide: ActivatedRoute, useValue: { queryParamMap: params } },
    { provide: AdminSessionService, useValue: { adminId: signal(admin), sessionId, capabilities: signal(capabilities) } },
  ] });
  const http = TestBed.inject(HttpTestingController);
  const makeFixture = () => TestBed.createComponent(BillingDocumentPageComponent);
  let fixture = makeFixture();
  const page = () => fixture.nativeElement as HTMLElement;
  const sync = () => fixture.detectChanges();
  const take = (method: string, url: string) => vi.waitFor(() => {
    sync();
    const [request] = http.match(request => request.url === url && request.method === method);
    expect(request, `${method} ${url}`).toBeDefined();
    return request;
  });
  const waitForText = (text: string) => vi.waitFor(() => { sync(); expect(page().textContent).toContain(text); });
  const load = async () => {
    (await take('GET', path)).flush(view);
    await vi.waitFor(() => { sync(); expect(page().querySelector('#order-heading')).not.toBeNull(); });
  };
  sync();
  if (capabilities.includes('billing')) await load();
  return { records, http, page, take, sync, waitForText, params, sessionId, load,
    reload: () => { fixture.destroy(); fixture = makeFixture(); sync(); } };
}

type Harness = Awaited<ReturnType<typeof open>>;

function review(h: Harness, fileBytes = bytes, name = metadata.fileName, size = fileBytes.length) {
  const page = h.page();
  const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
  const read = vi.fn(async () => Uint8Array.from(fileBytes).buffer);
  Object.defineProperty(input, 'files', { configurable: true, value: {
    item: () => ({ name, type: 'application/pdf', size, arrayBuffer: read }),
  } });
  for (const key of ['issuerName', 'issuerReference', 'issuedOn'] as const) {
    page.querySelector<HTMLInputElement>(`[name=${key}]`)!.value = metadata[key];
  }
  input.form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  return read;
}

async function post(h: Harness, suffix: string) {
  (await h.take('GET', '/api/admin/auth/sign-in/context')).flush({ requestToken: 'fixture-csrf' });
  const upload = await h.take('POST', path + suffix);
  expect(upload.request.headers.get('X-Admin-Session')).toBe(session);
  expect(upload.request.headers.get('X-Admin-CSRF')).toBe('fixture-csrf');
  return upload;
}

async function confirm(h: Harness) {
  review(h);
  const request = await post(h, '/preview');
  expect(request.request.body).toEqual(metadata);
  request.flush({ ...view, metadata, version: 1, previewDigest: 'b'.repeat(64) });
  await h.waitForText('Invoice metadata preview');
  expect(h.page().querySelectorAll('iframe, embed, object')).toHaveLength(0);
  click(h, 'Attach accounting invoice', false);
  h.page().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
  h.sync();
  click(h, 'Attach accounting invoice');
  return post(h, '');
}

function click(h: Harness, label: string, enabled = true) {
  const button = [...h.page().querySelectorAll('button')].find(candidate => candidate.textContent?.trim() === label);
  expect(button).toBeDefined();
  expect(button!.disabled).toBe(!enabled);
  if (enabled) button!.click();
}

describe('accounting invoice attachment', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('loads no order for a Support operator', async () => {
    const h = await open(['support']);
    await h.waitForText('Billing capability is required.');
    h.http.expectNone(request => request.url.startsWith('/api/admin'));
    h.http.verify();
  });

  it('rejects wrong names, file headers and oversized files before requesting preview', async () => {
    const h = await open();
    expect(review(h, bytes, '../invoice.pdf')).not.toHaveBeenCalled();
    await h.waitForText('Select a PDF with a valid file name.');
    expect(review(h, bytes, 'big.pdf', MAX_PDF_BYTES + 1)).not.toHaveBeenCalled();
    await h.waitForText('PDFs must be no larger than 10 MiB.');
    review(h, new TextEncoder().encode('<html>not pdf'));
    await h.waitForText('Select a valid PDF.');
    h.http.verify();
  });

  it('claims the original bytes before upload and keeps them across reload and a 404 lookup', async () => {
    const h = await open();
    const upload = await confirm(h);
    const form = upload.request.body as FormData;
    expect([...form.keys()]).toEqual(['command', 'content']);
    expect((form.get('content') as File).size).toBe(bytes.length);
    const { pending } = h.records.get(draftKey) as { pending: InvoiceIntent };
    expect(Array.from(new Uint8Array(pending.bytes))).toEqual(Array.from(bytes));
    expect(pending.operationKey).toBe(session);
    upload.error(new ProgressEvent('network error'));
    await h.waitForText('The upload is unresolved.');
    h.reload();
    await h.load();
    click(h, 'Check original upload');
    const recover = await h.take('GET', `${path}/operations/${session}`);
    recover.flush({ code: 'BillingDocument.OperationNotFound' }, { status: 404, statusText: 'Not Found' });
    await h.waitForText('The upload is unresolved.');
    click(h, 'Retry original upload');
    const retry = await post(h, '');
    expect((h.records.get(draftKey) as { pending: InvoiceIntent }).pending.operationKey).toBe(pending.operationKey);
    retry.flush(savedVersion(pending.operationKey));
    await h.waitForText('Accounting invoice attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('keeps a mismatched success receipt unresolved and ignores a reply from the previous account', async () => {
    const h = await open();
    (await confirm(h)).flush(savedVersion(session, { sha256: 'f'.repeat(64) }));
    await h.waitForText('The upload is unresolved.');
    click(h, 'Check original upload');
    const delayed = await h.take('GET', `${path}/operations/${session}`);
    h.params.next(convertToParamMap({ account: other, order }));
    const foreign = await h.take('GET', path.replace(account, other));
    foreign.flush({ code: 'BillingDocument.OrderNotFound' }, { status: 404, statusText: 'Not Found' });
    delayed.flush(savedVersion(session));
    await h.waitForText('The accepted order was not found for this account.');
    expect(h.page().textContent).not.toContain('Accounting invoice attached.');
    expect(h.records.get(draftKey)).toHaveProperty('pending');
    h.http.verify();
  });

  it('retains the draft for MFA and releases a definitive invalid-file refusal', async () => {
    const h = await open();
    (await confirm(h)).flush({ code: 'ManualPayment.RecentMfaRequired' }, { status: 403, statusText: 'Forbidden' });
    await h.waitForText('Sign in with MFA');
    expect(h.records.get(draftKey)).toHaveProperty('pending');
    click(h, 'Retry original upload');
    (await post(h, '')).flush({ code: 'BillingDocument.ContentInvalid' }, { status: 400, statusText: 'Bad Request' });
    await h.waitForText('Select a valid PDF.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });
});
