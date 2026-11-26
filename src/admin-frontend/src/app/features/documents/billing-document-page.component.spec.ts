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
const v2VersionId = '66666666-6666-4666-8666-666666666666';
const v3VersionId = '77777777-7777-4777-8777-777777777777';
const path = `/api/admin/accounts/${account}/orders/${order}/billing-documents`;
const draftKey = [admin, account, order].join('.');
const bytes = new TextEncoder().encode('%PDF-1.7\nClient-side header check only\n');
const sha256 = createHash('sha256').update(bytes).digest('hex');
const v2Bytes = new TextEncoder().encode('%PDF-1.7\nCorrected accounting invoice v2\n');
const v2Sha256 = createHash('sha256').update(v2Bytes).digest('hex');
const v3Bytes = new TextEncoder().encode('%PDF-1.7\nCorrected accounting invoice v3\n');
const v3Sha256 = createHash('sha256').update(v3Bytes).digest('hex');
const metadata = {
  kind: 'AccountingInvoice' as const, fileName: 'fixture.pdf', mediaType: 'application/pdf' as const,
  byteLength: bytes.length, sha256, issuerName: 'Fixture Accounting', issuerReference: 'INV-114', issuedOn: '2027-03-10',
};
const v2Metadata = {
  ...metadata, fileName: 'fixture-v2.pdf', byteLength: v2Bytes.length, sha256: v2Sha256, issuerReference: 'INV-114-CORR-1',
};
const v3Metadata = {
  ...metadata, fileName: 'fixture-v3.pdf', byteLength: v3Bytes.length, sha256: v3Sha256, issuerReference: 'INV-114-CORR-2',
};
const view = (documents: unknown[] = []) => ({
  schemaVersion: 1, accountId: account, orderId: order, paymentConfirmations: 1, documents,
  order: { planName: 'Fixture', period: 'Month', amountPaise: 12000, currency: 'INR', revision: 1,
    snapshotHash: 'a'.repeat(64), acceptedAt: '2027-03-10T01:00:00Z' },
});
const savedVersion = (operationKey: string, overrides = {}) => ({
  schemaVersion: 1, ...metadata, accountId: account, orderId: order, operationKey,
  version: 1, documentId: order, versionId: other, supersedesVersionId: null, supersessionReason: null,
  uploadedBy: admin, uploadedAt: '2027-03-10T02:00:00Z', ...overrides,
});

async function open(capabilities = ['billing'], initialDocs: unknown[] = [], uuids = [session]) {
  const records = installStorage();
  const subtle = {
    digest: async (_algorithm: string, input: ArrayBuffer) => Uint8Array.from(createHash('sha256')
      .update(new Uint8Array(input)).digest()).buffer,
  };
  let nextUuid = 0;
  vi.stubGlobal('crypto', { subtle, randomUUID: () => uuids[Math.min(nextUuid++, uuids.length - 1)] });
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

function submitCorrection(h: Harness, meta: typeof v2Metadata, fileBytes: Uint8Array, reason: string) {
  const page = h.page();
  const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
  const read = vi.fn(async () => Uint8Array.from(fileBytes).buffer);
  Object.defineProperty(input, 'files', { configurable: true, value: {
    item: () => ({ name: meta.fileName, type: 'application/pdf', size: fileBytes.length, arrayBuffer: read }),
  } });
  page.querySelector<HTMLInputElement>('[name=issuerName]')!.value = meta.issuerName;
  page.querySelector<HTMLInputElement>('[name=issuerReference]')!.value = meta.issuerReference;
  page.querySelector<HTMLInputElement>('[name=issuedOn]')!.value = meta.issuedOn;
  page.querySelector<HTMLTextAreaElement>('[name=reason]')!.value = reason;
  input.form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
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
  request.flush({ ...view(), metadata, version: 1, previewDigest: 'b'.repeat(64) });
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
  expect(button, `button "${label}"`).toBeDefined();
  expect(button!.disabled).toBe(!enabled);
  if (enabled) button!.click();
}

describe('accounting invoice attachment and supersession', () => {
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
    const v1 = savedVersion(pending.operationKey);
    retry.flush(v1);
    (await h.take('GET', path)).flush(view([v1]));
    await h.waitForText('Accounting invoice attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('recovers a correction and retains earlier versions', async () => {
    const v1 = savedVersion('99999999-9999-4999-8999-999999999991');
    const op2 = '99999999-9999-4999-8999-999999999992';
    const op3 = '99999999-9999-4999-8999-999999999993';
    const h = await open(['billing'], [v1], [op2, op3]);

    click(h, 'Supersede current invoice');
    h.sync();
    submitCorrection(h, v2Metadata, v2Bytes, 'Fix GSTIN on customer invoice');
    const previewReq = await post(h, `/${other}/supersede/preview`);
    expect(previewReq.request.body).toEqual({ ...v2Metadata, reason: 'Fix GSTIN on customer invoice' });
    previewReq.flush({
      ...view([v1]), documentId: order, supersedesVersionId: other, supersedesVersion: 1, version: 2,
      metadata: v2Metadata, reason: 'Fix GSTIN on customer invoice', previewDigest: 'c'.repeat(64),
    });
    await h.waitForText('Correction preview');
    expect(h.page().textContent).toContain('INV-114');
    click(h, 'Attach corrective invoice', false);
    h.page().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    h.sync();
    click(h, 'Attach corrective invoice');

    const upload = await post(h, `/${other}/supersede`);
    const savedDraft = h.records.get(draftKey) as { pending: InvoiceIntent };
    expect(savedDraft.pending.supersedesVersionId).toBe(other);
    expect(savedDraft.pending.reason).toBe('Fix GSTIN on customer invoice');
    expect(Array.from(new Uint8Array(savedDraft.pending.bytes))).toEqual(Array.from(v2Bytes));
    upload.error(new ProgressEvent('network error'));
    await h.waitForText('The upload is unresolved.');

    h.reload();
    await h.load([v1]);
    click(h, 'Check original correction');
    const v2 = savedVersion(op2, {
      ...v2Metadata, version: 2, versionId: v2VersionId, supersedesVersionId: other,
      supersessionReason: 'Fix GSTIN on customer invoice',
    });
    (await h.take('GET', `${path}/operations/${op2}`)).flush(v2);
    (await h.take('GET', path)).flush(view([v2, v1]));
    await h.waitForText('Corrective invoice version attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    expect([...h.page().querySelectorAll('.adm-version-badge')].map(el => el.textContent?.trim()))
      .toEqual(['Current', 'Historical']);

    // Issue v3 from the same screen after settlement reloaded revision & canonical history.
    click(h, 'Supersede current invoice');
    h.sync();
    submitCorrection(h, v3Metadata, v3Bytes, 'Second reissue');
    (await post(h, `/${v2VersionId}/supersede/preview`)).flush({
      ...view([v2, v1]), documentId: order, supersedesVersionId: v2VersionId, supersedesVersion: 2, version: 3,
      metadata: v3Metadata, reason: 'Second reissue', previewDigest: 'd'.repeat(64),
    });
    await h.waitForText('Correction preview');
    h.page().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    h.sync();
    click(h, 'Attach corrective invoice');
    const v3 = savedVersion(op3, {
      ...v3Metadata, version: 3, versionId: v3VersionId, supersedesVersionId: v2VersionId,
      supersessionReason: 'Second reissue',
    });
    (await post(h, `/${v2VersionId}/supersede`)).flush(v3);
    (await h.take('GET', path)).flush(view([v3, v2, v1]));
    await h.waitForText('Corrective invoice version attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 2 });
    expect([...h.page().querySelectorAll('.adm-version-badge')].map(el => el.textContent?.trim()))
      .toEqual(['Current', 'Historical', 'Historical']);
    h.http.verify();
  });

  it('clears a definite stale-head refusal, reloads history and preserves untagged initial-upload drafts', async () => {
    const v1 = savedVersion('99999999-9999-4999-8999-999999999991');
    const v2 = savedVersion('99999999-9999-4999-8999-999999999992', {
      ...v2Metadata, version: 2, versionId: v2VersionId, supersedesVersionId: other, supersessionReason: 'Concurrent fix',
    });
    const h = await open(['billing'], [v1]);
    click(h, 'Supersede current invoice');
    h.sync();
    submitCorrection(h, v2Metadata, v2Bytes, 'My correction');
    (await post(h, `/${other}/supersede/preview`)).flush({
      ...view([v1]), documentId: order, supersedesVersionId: other, supersedesVersion: 1, version: 2,
      metadata: v2Metadata, reason: 'My correction', previewDigest: 'e'.repeat(64),
    });
    await h.waitForText('Correction preview');
    h.page().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    h.sync();
    click(h, 'Attach corrective invoice');
    (await post(h, `/${other}/supersede`)).flush(
      { code: 'BillingDocument.PredecessorStale' }, { status: 409, statusText: 'Conflict' },
    );
    (await h.take('GET', path)).flush(view([v2, v1]));
    await h.waitForText('A newer invoice version already exists.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    // Existing untagged initial-upload draft (no `mode` field) still recovers on the initial upload route.
    h.records.set(draftKey, {
      revision: 1,
      pending: { operationKey: session, metadata, previewDigest: 'b'.repeat(64), bytes: Uint8Array.from(bytes).buffer },
    });
    h.reload();
    await h.load([]);
    await h.waitForText('Saved upload operation');
    click(h, 'Retry original upload');
    const legacy = savedVersion(session);
    (await post(h, '')).flush(legacy);
    (await h.take('GET', path)).flush(view([legacy]));
    await h.waitForText('Accounting invoice attached.');
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
