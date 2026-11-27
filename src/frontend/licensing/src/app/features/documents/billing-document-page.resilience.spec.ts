import { afterEach, describe, expect, it, vi } from 'vitest';
import type { InvoiceIntent } from './billing-document-file';
import {
  badges,
  bytes,
  click,
  confirm,
  confirmCorrectionUpload,
  draftKey,
  open,
  openCorrectionPreview,
  other,
  path,
  post,
  savedVersion,
  session,
  sha256,
  submitCorrection,
  v2Bytes,
  v2Metadata,
  v2Sha256,
  v2VersionId,
  view,
  type Harness,
} from './billing-document-page.test-utils';

const op1 = '99999999-9999-4999-8999-999999999991';
const op2 = '99999999-9999-4999-8999-999999999992';
const v1Doc = () => savedVersion(op1);
const v2Doc = (reason = 'Fix GSTIN') => savedVersion(op2, {
  ...v2Metadata, version: 2, versionId: v2VersionId, supersedesVersionId: other, supersessionReason: reason,
});

function assertNoProtectedFinanceContent(h: Harness, tokens: readonly string[]) {
  const text = h.page().textContent ?? '';
  for (const token of tokens) expect(text).not.toContain(token);
  expect(badges(h)).toEqual([]);
  expect(h.page().querySelectorAll('button')).toHaveLength(0);
}

describe('accounting invoice local draft cleanup failures (R002)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('preserves a confirmed correction and hides stale Current when settle rejects, then retries without a mutation', async () => {
    const v1 = v1Doc(), v2 = v2Doc();
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    const upload = await confirmCorrectionUpload(h, other);
    h.failNext('readwrite');
    upload.flush(v2);

    await h.waitForText('Corrective invoice version attached.');
    await h.waitForText('Invoice history awaiting refresh');
    expect(h.page().textContent).toContain('INV-114-CORR-1');
    expect(h.page().textContent).toContain(op2);
    expect(h.page().textContent).not.toContain('The upload is unresolved.');
    expect(h.page().textContent).not.toContain('Supersede current invoice');
    expect(h.page().textContent).not.toContain('Retry original correction');
    expect(badges(h)).toEqual([]);
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(op2);

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v2, v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical']); });
    expect(h.page().textContent).toContain('Corrective invoice version attached.');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('preserves a confirmed correction when settle succeeds and read rejects, then refreshes without a mutation', async () => {
    const v1 = v1Doc(), v2 = v2Doc();
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    const upload = await confirmCorrectionUpload(h, other);
    h.failNext('readonly');
    upload.flush(v2);

    await h.waitForText('Corrective invoice version attached.');
    await h.waitForText('Invoice history awaiting refresh');
    expect(h.page().textContent).toContain('INV-114-CORR-1');
    expect(h.page().textContent).toContain(op2);
    expect(h.page().textContent).not.toContain('The upload is unresolved.');
    expect(badges(h)).toEqual([]);
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v2, v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical']); });
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('preserves a confirmed recovery lookup and initial attachment when local cleanup fails', async () => {
    const h = await open();
    const v1 = savedVersion(session);
    const upload = await confirm(h);
    upload.error(new ProgressEvent('network error'));
    await h.waitForText('The upload is unresolved.');

    click(h, 'Check original upload');
    const lookup = await h.take('GET', `${path}/operations/${session}`);
    h.failNext('readwrite');
    lookup.flush(v1);

    await h.waitForText('Accounting invoice attached.');
    await h.waitForText('Invoice history awaiting refresh');
    expect(h.page().textContent).toContain('INV-114');
    expect(h.page().textContent).toContain(session);
    expect(h.page().textContent).not.toContain('Attach an invoice');
    expect(h.page().textContent).not.toContain('Retry original upload');
    expect(badges(h)).toEqual([]);
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(session);

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current']); });
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('preserves an initial attachment when settle succeeds and read rejects, then refreshes without a mutation', async () => {
    const h = await open();
    const v1 = savedVersion(session);
    const upload = await confirm(h);
    h.failNext('readonly');
    upload.flush(v1);

    await h.waitForText('Accounting invoice attached.');
    await h.waitForText('Invoice history awaiting refresh');
    expect(h.page().textContent).toContain('INV-114');
    expect(h.page().textContent).not.toContain('Attach an invoice');
    expect(badges(h)).toEqual([]);
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current']); });
    h.http.verify();
  });

  it('invalidates stale Current immediately on 409 PredecessorStale when settle rejects, then retries cleanup without a mutation', async () => {
    const v1 = v1Doc(), v2 = v2Doc('Concurrent fix');
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'My fix', 'c'.repeat(64));
    const upload = await confirmCorrectionUpload(h, other);
    h.failNext('readwrite');
    upload.flush({ code: 'BillingDocument.PredecessorStale' }, { status: 409, statusText: 'Conflict' });

    await h.waitForText('A newer invoice version already exists.');
    await h.waitForText('Invoice history awaiting refresh');
    expect(badges(h)).toEqual([]);
    expect(h.page().textContent).toContain(op2);
    expect(h.page().textContent).not.toContain('Supersede current invoice');
    expect(h.page().textContent).not.toContain('Retry original correction');
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(op2);

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v2, v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical']); });
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });

  it('invalidates stale Current immediately on 409 PredecessorStale when settle succeeds and read rejects', async () => {
    const v1 = v1Doc(), v2 = v2Doc('Concurrent fix');
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'My fix', 'c'.repeat(64));
    const upload = await confirmCorrectionUpload(h, other);
    h.failNext('readonly');
    upload.flush({ code: 'BillingDocument.PredecessorStale' }, { status: 409, statusText: 'Conflict' });

    await h.waitForText('A newer invoice version already exists.');
    await h.waitForText('Invoice history awaiting refresh');
    expect(badges(h)).toEqual([]);
    expect(h.page().textContent).toContain(op2);
    expect(h.page().textContent).not.toContain('Supersede current invoice');
    expect(h.page().textContent).not.toContain('Retry original correction');
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });

    click(h, 'Refresh invoice history');
    (await h.take('GET', path)).flush(view([v2, v1]));
    await vi.waitFor(() => { h.sync(); expect(badges(h)).toEqual(['Current', 'Historical']); });
    expect(h.records.get(draftKey)).toEqual({ revision: 1 });
    h.http.verify();
  });
});

describe('accounting invoice authority loss and ended sessions (R007)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('hides confirmed receipt metadata and finance actions on 403 Billing denial with session snapshot unchanged', async () => {
    const v1 = v1Doc(), v2 = v2Doc('Fix GSTIN');
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    (await confirmCorrectionUpload(h, other)).flush(v2);
    (await h.take('GET', path)).flush({ code: 'ManualPayment.AccessDenied' }, { status: 403, statusText: 'Forbidden' });

    await h.waitForText('Billing capability is required.');
    assertNoProtectedFinanceContent(h, [
      'Fixture Accounting', 'INV-114-CORR-1', 'INV-114', 'Fix GSTIN', v2Sha256, sha256, op2,
      'Corrective invoice version attached.', 'Refresh invoice history', 'Supersede current invoice', 'Load order',
    ]);
    h.http.verify();
  });

  it('hides loaded invoice history and actions on 403 Billing denial while loading older versions', async () => {
    const v2 = v2Doc('Fix GSTIN on older chain');
    const h = await open(['billing'], [v2]);

    click(h, 'Load older versions');
    (await h.take('GET', `${path}/before/2`)).flush(
      { code: 'ManualPayment.AccessDenied' }, { status: 403, statusText: 'Forbidden' },
    );

    await h.waitForText('Billing capability is required.');
    assertNoProtectedFinanceContent(h, [
      'Fixture Accounting', 'INV-114-CORR-1', 'Fix GSTIN on older chain', v2Sha256,
      'Load older versions', 'Supersede current invoice', 'Load order',
    ]);
    h.http.verify();
  });

  it('hides protected state when reloadAfterConflict receives a 403 Billing denial', async () => {
    const v1 = v1Doc();
    const h = await open(['billing'], [v1]);

    click(h, 'Supersede current invoice');
    h.sync();
    submitCorrection(h, v2Metadata, v2Bytes, 'Concurrent fix');
    (await post(h, `/${other}/supersede/preview`)).flush(
      { code: 'BillingDocument.PredecessorStale' }, { status: 409, statusText: 'Conflict' },
    );
    (await h.take('GET', path)).flush({ code: 'ManualPayment.AccessDenied' }, { status: 403, statusText: 'Forbidden' });

    await h.waitForText('Billing capability is required.');
    expect(h.page().textContent).not.toContain('A newer invoice version already exists.');
    assertNoProtectedFinanceContent(h, ['Fixture Accounting', 'INV-114', sha256, 'Refresh invoice history', 'Load order']);
    h.http.verify();
  });

  it('hides protected metadata and preserves the durable draft on 401 ended session', async () => {
    const v1 = v1Doc(), v2 = v2Doc('Fix GSTIN');
    const h = await open(['billing'], [v1], [op2], { withSettle: true });

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    (await confirmCorrectionUpload(h, other)).flush({ code: 'Admin.SessionEnded' }, { status: 401, statusText: 'Unauthorized' });

    await h.waitForText('Sign in with MFA');
    expect(h.settleSpy).toHaveBeenCalledWith('ended');
    assertNoProtectedFinanceContent(h, [
      'Fixture Accounting', 'INV-114-CORR-1', 'INV-114', 'Fix GSTIN', v2Sha256, sha256, op2,
      'Saved correction operation', 'Check original correction', 'Retry original correction', 'Load order',
    ]);
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(op2);

    // Re-authenticating with a new live session restores access and the durable draft.
    h.adminId.set('33333333-3333-4333-8333-333333333333');
    h.sessionId.set('88888888-8888-4888-8888-888888888888');
    h.grants.set(['billing']);
    await h.load([v2, v1]);
    await h.waitForText('Saved correction operation');
    h.http.verify();
  });

  it('hides confirmed receipt on 401 ended session even when the session snapshot stays unchanged', async () => {
    const v1 = v1Doc(), v2 = v2Doc('Fix GSTIN');
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    (await confirmCorrectionUpload(h, other)).flush(v2);
    (await h.take('GET', path)).flush({ code: 'Admin.SessionEnded' }, { status: 401, statusText: 'Unauthorized' });

    await h.waitForText('Sign in with MFA');
    assertNoProtectedFinanceContent(h, [
      'Fixture Accounting', 'INV-114-CORR-1', 'INV-114', 'Fix GSTIN', v2Sha256, op2,
      'Corrective invoice version attached.', 'Refresh invoice history', 'Load order',
    ]);
    h.http.verify();
  });

  it('hides stored draft details while preserving IndexedDB on denied restore (403 and 401)', async () => {
    const storedIntent: InvoiceIntent = {
      mode: 'supersede', operationKey: op2, metadata: v2Metadata, previewDigest: 'c'.repeat(64),
      bytes: Uint8Array.from(bytes).buffer, supersedesVersionId: other, supersedesVersion: 1,
      supersedesReference: 'INV-114', reason: 'Private draft reason',
    };
    const h = await open(['billing'], [], [op2], { autoLoad: false, initialRecord: { revision: 0, pending: storedIntent } });

    (await h.take('GET', path)).flush({ code: 'ManualPayment.AccessDenied' }, { status: 403, statusText: 'Forbidden' });
    await h.waitForText('Billing capability is required.');
    assertNoProtectedFinanceContent(h, [
      'Saved correction operation', 'INV-114-CORR-1', 'INV-114', 'Private draft reason', op2, v2Sha256, 'Load order',
    ]);
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(op2);

    h.reload();
    (await h.take('GET', path)).flush({ code: 'Admin.SessionEnded' }, { status: 401, statusText: 'Unauthorized' });
    await h.waitForText('Sign in with MFA');
    assertNoProtectedFinanceContent(h, [
      'Saved correction operation', 'INV-114-CORR-1', 'INV-114', 'Private draft reason', op2, v2Sha256, 'Load order',
    ]);
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(op2);
    h.http.verify();
  });

  it('keeps recent-MFA write refusal recoverable without hiding the saved correction draft', async () => {
    const v1 = v1Doc();
    const h = await open(['billing'], [v1], [op2]);

    await openCorrectionPreview(h, other, 1, [v1], v2Metadata, v2Bytes, 'Fix GSTIN', 'c'.repeat(64));
    (await confirmCorrectionUpload(h, other)).flush(
      { code: 'ManualPayment.RecentMfaRequired' }, { status: 403, statusText: 'Forbidden' },
    );

    await h.waitForText('Sign in with MFA');
    expect(h.page().textContent).toContain('Saved correction operation');
    expect(h.page().textContent).toContain(op2);
    expect((h.records.get(draftKey) as { pending?: InvoiceIntent }).pending?.operationKey).toBe(op2);
    h.http.verify();
  });
});
