import { convertToParamMap } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MAX_PDF_BYTES, type InvoiceIntent } from './billing-document-file';
import {
  account,
  badges,
  bytes,
  click,
  confirm,
  confirmCorrectionUpload,
  draftKey,
  metadata,
  open,
  openCorrectionPreview,
  order,
  other,
  path,
  post,
  review,
  savedVersion,
  session,
  submitCorrection,
  v2Bytes,
  v2Metadata,
  v2VersionId,
  v3Bytes,
  v3Metadata,
  v3VersionId,
  view,
} from './billing-document-page.test-utils';

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
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current']); });
    expect(h.page().textContent).toContain('Accounting invoice attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('recovers a correction and retains earlier versions', async () => {
    const v1 = savedVersion('99999999-9999-4999-8999-999999999991');
    const op2 = '99999999-9999-4999-8999-999999999992';
    const op3 = '99999999-9999-4999-8999-999999999993';
    const h = await open(['billing'], [v1], [op2, op3]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN on customer invoice', 'c'.repeat(64));
    expect(h.page().textContent).toContain('INV-114');
    const upload = await confirmCorrectionUpload(h, other);
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
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical']); });
    expect(h.page().textContent).toContain('Corrective invoice version attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    // Issue v3 from the same screen after settlement reloaded revision & canonical history.
    await openCorrectionPreview(h, v2VersionId, 2, [v2, v1], v3Metadata, v3Bytes, 'Second reissue', 'd'.repeat(64));
    const v3 = savedVersion(op3, {
      ...v3Metadata, version: 3, versionId: v3VersionId, supersedesVersionId: v2VersionId,
      supersessionReason: 'Second reissue',
    });
    (await confirmCorrectionUpload(h, v2VersionId)).flush(v3);
    (await h.take('GET', path)).flush(view([v3, v2, v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical', 'Historical']); });
    expect(h.page().textContent).toContain('Corrective invoice version attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 2 });
    h.http.verify();
  });

  it('keeps a confirmed correction visible when history refresh fails and retries without a second write', async () => {
    const v1 = savedVersion('99999999-9999-4999-8999-999999999991');
    const op2 = '99999999-9999-4999-8999-999999999992';
    const v2 = savedVersion(op2, {
      ...v2Metadata, version: 2, versionId: v2VersionId, supersedesVersionId: other, supersessionReason: 'Fix GSTIN',
    });
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    (await confirmCorrectionUpload(h, other)).flush(v2);
    (await h.take('GET', path)).flush({ code: 'Service.Unavailable' }, { status: 503, statusText: 'Service Unavailable' });

    await h.waitForText('Version history could not be refreshed. Retry loading history.');
    expect(h.page().textContent).toContain('Corrective invoice version attached.');
    expect(h.page().textContent).toContain('INV-114-CORR-1');
    expect(badges(h)).toEqual([]);
    expect(h.page().textContent).not.toContain('Supersede current invoice');
    expect(h.page().textContent).not.toContain('Attach an invoice');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v2, v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical']); });
    expect(h.page().textContent).toContain('Corrective invoice version attached.');
    h.http.verify();
  });

  it('keeps a confirmed recovery and initial attachment visible when history refresh fails', async () => {
    const h = await open();
    const v1 = savedVersion(session);
    const upload = await confirm(h);
    upload.error(new ProgressEvent('network error'));
    await h.waitForText('The upload is unresolved.');

    click(h, 'Check original upload');
    (await h.take('GET', `${path}/operations/${session}`)).flush(v1);
    (await h.take('GET', path)).flush({ code: 'Service.Unavailable' }, { status: 503, statusText: 'Service Unavailable' });

    await h.waitForText('Version history could not be refreshed. Retry loading history.');
    expect(h.page().textContent).toContain('Accounting invoice attached.');
    expect(h.page().textContent).toContain('INV-114');
    expect(h.page().textContent).not.toContain('Attach an invoice');
    expect(badges(h)).toEqual([]);
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current']); });
    h.http.verify();
  });

  it('reloads canonical head on 409 PredecessorStale during preview and targets v2 next', async () => {
    const v1 = savedVersion('99999999-9999-4999-8999-999999999991');
    const v2 = savedVersion('99999999-9999-4999-8999-999999999992', {
      ...v2Metadata, version: 2, versionId: v2VersionId, supersedesVersionId: other, supersessionReason: 'Concurrent fix',
    });
    const h = await open(['billing'], [v1]);

    click(h, 'Supersede current invoice');
    h.sync();
    submitCorrection(h, v2Metadata, v2Bytes, 'My correction');
    (await post(h, `/${other}/supersede/preview`)).flush(
      { code: 'BillingDocument.PredecessorStale' }, { status: 409, statusText: 'Conflict' },
    );
    (await h.take('GET', path)).flush(view([v2, v1]));

    await h.waitForText('A newer invoice version already exists.');
    expect(badges(h)).toEqual(['Current', 'Historical']);
    expect(h.page().querySelector('[data-version="2"]')?.textContent).toContain('Current');
    expect(h.page().querySelector('[data-version="1"]')?.textContent).toContain('Historical');
    expect(h.page().textContent).not.toContain('Correction preview');

    await openCorrectionPreview(h, v2VersionId, 2, [v2, v1], v3Metadata, v3Bytes, 'Correction on v2', 'd'.repeat(64));
    expect(h.page().textContent).toContain('INV-114-CORR-1');
    h.http.verify();
  });

  it('clears a definite stale-head refusal and reloads history', async () => {
    const v1 = savedVersion('99999999-9999-4999-8999-999999999991');
    const v2 = savedVersion('99999999-9999-4999-8999-999999999992', {
      ...v2Metadata, version: 2, versionId: v2VersionId, supersedesVersionId: other, supersessionReason: 'Concurrent fix',
    });
    const h = await open(['billing'], [v1]);
    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'My correction', 'e'.repeat(64));
    (await confirmCorrectionUpload(h, other)).flush(
      { code: 'BillingDocument.PredecessorStale' }, { status: 409, statusText: 'Conflict' },
    );
    (await h.take('GET', path)).flush(view([v2, v1]));
    await h.waitForText('A newer invoice version already exists.');
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
