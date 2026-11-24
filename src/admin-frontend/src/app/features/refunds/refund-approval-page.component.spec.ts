import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { RefundIntakeApi } from '../payments/refunds/refund-intake-api.service';
import { RefundIntakePageComponent } from '../payments/refunds/refund-intake-page.component';
import {
  account, admin, approvalPath, base, item, loadApprovalPage, open, otherAccount, preview, receipt, request, reviewed,
  submitButton,
} from './refund-approval.test-utils';

afterEach(() => {
  TestBed.resetTestingModule();
  vi.unstubAllGlobals();
});

describe('approvalPreviewAndScopedRecovery', () => {
  it('starts from a Requested intake row and routes to the approval page', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap: new BehaviorSubject(convertToParamMap({ account })) } },
      { provide: AdminSessionService, useValue: { adminId: signal(admin), sessionId: signal('s'), capabilities: signal(['billing']) } },
      { provide: RefundIntakeApi, useValue: { list: async () => ({ schemaVersion: 1, accountId: account, nextCursor: null,
        items: [item, { ...item, refundRequestId: request.replace(/1$/, '2'), status: 'Pending' }] }) } },
    ] });
    const intake = TestBed.createComponent(RefundIntakePageComponent);
    const links = await vi.waitFor(() => {
      intake.detectChanges();
      const rows = intake.nativeElement.querySelectorAll('[data-testid="refund-intake-row"]');
      expect(rows.length).toBe(2);
      return Array.from(rows as NodeListOf<HTMLElement>).map(row => row.querySelector('[data-testid="refund-intake-approve"]'));
    });
    // Only an undecided request offers the approval; a decided one never does.
    expect(links[0]?.getAttribute('href')).toBe(approvalPath);
    expect(intake.nativeElement.querySelector('[data-testid="refund-intake-view"]')?.getAttribute('href'))
      .toBe(approvalPath.replace('000001/approve', '000002/approve'));
    TestBed.resetTestingModule();
    expect(await loadApprovalPage()).not.toBeNull();
  });

  it('requires a treatment, a reason and a matching preview before the approval can be sent', async () => {
    const harness = await open();
    await harness.shown('refund-approval-form');
    const previewButton = harness.find<HTMLButtonElement>('refund-approval-preview')!;
    // No treatment is preselected and nothing can be previewed or approved yet.
    expect(harness.page.querySelectorAll('input[name="treatment"]:checked').length).toBe(0);
    expect(previewButton.disabled).toBe(true);
    expect(submitButton(harness)).toBeNull();
    harness.set('#refund-approval-amount', '45.00');
    harness.choose('EndAt');
    harness.set('#refund-approval-reason', 'Reason');
    expect(previewButton.disabled).toBe(true);
    harness.set('#refund-approval-effective', '2028-02-10T10:00:00');
    expect(previewButton.disabled).toBe(false);
    harness.choose('Preserve');

    const body = await reviewed(harness);
    expect(body).toEqual({ amountPaise: 4500, treatment: 'Preserve', effectiveAt: null });
    const impact = harness.find('refund-approval-impact')!.textContent!;
    for (const amount of ['₹120.00', '₹60.00', '₹45.00', '₹75.00']) expect(impact).toContain(amount);
    expect(Array.from(harness.page.querySelectorAll('[data-testid="refund-approval-shop"]')).map(shop => shop.textContent!.trim()))
      .toEqual(['Refund Central', expect.stringContaining('Refund East')]);
    expect(impact).toContain('refund-2028');
    expect(impact).toContain('Access is preserved');
    expect(submitButton(harness)!.disabled).toBe(false);

    // Any change after the preview no longer matches what was reviewed.
    harness.set('#refund-approval-amount', '46.00');
    expect(submitButton(harness)).toBeNull();
    expect(harness.find('refund-approval-impact')).toBeNull();
  });

  it('persists the frozen command before sending it and shows the saved receipt', async () => {
    const harness = await open();
    await reviewed(harness);
    submitButton(harness)!.click();
    const sent = await harness.post('/approvals');
    const command = sent.request.body as Record<string, unknown>;
    expect(command).toEqual({ operationKey: expect.stringMatching(/^[0-9a-f-]{36}$/), amountPaise: 4500,
      reason: 'Duplicate branch purchased before go-live', treatment: 'Preserve', effectiveAt: null, expectedVersion: 0,
      impactDigest: preview.impactDigest, confirmed: true });
    expect(sent.request.headers.get('X-Admin-CSRF')).toBe('csrf');
    expect(sent.request.headers.get('X-Admin-Session')).toBe('session-1');
    // The exact command was durable before the request left.
    expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: expect.objectContaining(command) });

    sent.flush(receipt(command['operationKey'] as string), { status: 201, statusText: 'Created' });
    const saved = await harness.shown('refund-approval-receipt');
    expect(harness.find('refund-approval-status')?.textContent).toContain('Pending');
    expect(saved.textContent).toContain('₹45.00');
    expect(saved.textContent).toContain('₹75.00');
    expect(saved.textContent).toContain('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb');
    expect(saved.textContent).toContain('cccccccc-cccc-4ccc-8ccc-cccccccccccc');
    expect(harness.records.get(harness.key)).toEqual({ revision: 1 });
    expect(harness.find('refund-approval-form')).toBeNull();
  });

  it.each(['Refund.PreviewStale', 'Refund.AmountExceedsRemaining', 'Refund.AccessTreatmentUnavailable'])(
    'releases the command after the definite refusal %s and requires a new preview', async code => {
      const harness = await open();
      await reviewed(harness);
      submitButton(harness)!.click();
      (await harness.post('/approvals')).flush({ code }, { status: 409, statusText: 'Conflict' });
      await vi.waitFor(() => {
        harness.update();
        expect(harness.page.querySelector('[role="alert"]')?.textContent).toContain('Review the current preview');
      });
      expect(harness.records.get(harness.key)).toEqual({ revision: 1 });
      expect(harness.find('refund-approval-impact')).toBeNull();
      expect(submitButton(harness)).toBeNull();
      expect(harness.find('refund-approval-form')).not.toBeNull();
    },
  );

  it('keeps an ambiguous approval and recovers it by lookup or an exact retry of the original key', async () => {
    const harness = await open();
    await reviewed(harness);
    submitButton(harness)!.click();
    const sent = await harness.post('/approvals');
    const command = sent.request.body as Record<string, unknown>;
    sent.error(new ProgressEvent('error'));
    // Wait for the settled unknown outcome, not just the pending panel that appears while the request is in flight.
    await vi.waitFor(() => {
      harness.update();
      expect(harness.page.querySelector('[role="alert"]')?.textContent).toContain('outcome is unknown');
    });
    const pending = await harness.shown('refund-approval-pending');
    expect(pending.textContent).toContain(command['operationKey'] as string);
    expect(harness.find('refund-approval-form')).toBeNull();

    // A lookup that finds nothing proves nothing: the original key is retained.
    harness.find<HTMLButtonElement>('refund-approval-check')!.click();
    (await harness.take('GET', `/approvals/operations/${command['operationKey']}`))
      .flush({ code: 'Refund.OperationNotFound' }, { status: 404, statusText: 'Not Found' });
    await vi.waitFor(() => {
      harness.update();
      expect(harness.page.querySelector('[role="alert"]')?.textContent).toContain('original key is kept');
    });
    expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: expect.objectContaining(command) });

    harness.find<HTMLButtonElement>('refund-approval-retry')!.click();
    const retried = await harness.post('/approvals');
    expect(retried.request.body).toEqual(command);
    retried.flush(receipt(command['operationKey'] as string));
    await harness.shown('refund-approval-receipt');
    expect(harness.records.get(harness.key)).toEqual({ revision: 1 });
  });

  it('shows another tab’s unresolved approval after reload and sends nothing new', async () => {
    const original = { operationKey: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', amountPaise: 4500, reason: 'Duplicate branch purchased before go-live',
      treatment: 'Preserve', effectiveAt: null, expectedVersion: 0, impactDigest: preview.impactDigest, confirmed: true };
    const harness = await open({ pending: original });
    const pending = await harness.shown('refund-approval-pending');
    expect(pending.textContent).toContain(original.operationKey);
    expect(pending.textContent).toContain('₹45.00');
    expect(harness.find('refund-approval-form')).toBeNull();

    harness.find<HTMLButtonElement>('refund-approval-check')!.click();
    (await harness.take('GET', `/approvals/operations/${original.operationKey}`)).flush(receipt(original.operationKey));
    await harness.shown('refund-approval-receipt');
    expect(harness.find('refund-approval-status')?.textContent).toContain('Pending');
    harness.http.verify();
  });

  it('never settles a receipt whose identity differs from the frozen command', async () => {
    const harness = await open();
    await reviewed(harness);
    submitButton(harness)!.click();
    const sent = await harness.post('/approvals');
    const command = sent.request.body as Record<string, unknown>;
    sent.flush(receipt(command['operationKey'] as string, { adminId: otherAccount }));
    await harness.shown('refund-approval-pending');
    expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: expect.objectContaining(command) });
    expect(harness.find('refund-approval-receipt')).toBeNull();
  });

  it('keeps the original approval when MFA is no longer recent and points to a scoped sign-in', async () => {
    const harness = await open();
    await reviewed(harness);
    submitButton(harness)!.click();
    const sent = await harness.post('/approvals');
    sent.flush({ code: 'ManualPayment.RecentMfaRequired' }, { status: 403, statusText: 'Forbidden' });
    await harness.shown('refund-approval-pending');
    const link = await harness.shown('refund-approval-sign-in');
    expect(link.getAttribute('href')).toBe(`/auth/sign-in?refundAccount=${account}&refundRequest=${request}`);
    expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: expect.objectContaining(sent.request.body) });
  });

  it.each(['account', 'admin', 'session'] as const)(
    'ignores an answer that arrives after the %s changed', async scope => {
      const harness = await open();
      await reviewed(harness);
      submitButton(harness)!.click();
      const sent = await harness.post('/approvals');
      const command = sent.request.body as Record<string, unknown>;
      if (scope === 'account') harness.params.next(convertToParamMap({ account: otherAccount, request }));
      else if (scope === 'admin') harness.adminId.set(otherAccount);
      else harness.sessionId.set('session-2');
      harness.update();
      sent.flush(receipt(command['operationKey'] as string), { status: 201, statusText: 'Created' });
      await Promise.resolve();
      harness.update();
      // The late answer neither shows a receipt in the new scope nor settles the original command.
      expect(harness.find('refund-approval-receipt')).toBeNull();
      expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: expect.objectContaining(command) });
      if (scope !== 'session') expect(harness.find('refund-approval-pending')).toBeNull();
    },
  );

  it('offers no approval for a request that already has a decision', async () => {
    const harness = await open({ status: 'Pending' });
    const decided = await harness.shown('refund-approval-decided');
    expect(decided.textContent).toContain('already has a decision');
    expect(harness.find('refund-approval-form')).toBeNull();
    expect(base).toContain(account);
  });
});

describe('supported adjustment amounts', () => {
  it.each([['9999999999.99', true], ['10000000000.00', true], ['10000000000.01', false]] as const)(
    'allows a preview for %s only within the payment ceiling', async (rupees, valid) => {
      const harness = await open();
      await harness.shown('refund-approval-form');
      harness.set('#refund-approval-amount', rupees);
      harness.set('#refund-approval-reason', 'Approved correction');
      harness.choose('Preserve');
      expect(harness.find<HTMLButtonElement>('refund-approval-preview')!.disabled).toBe(!valid);
    });
});

describe('approval evidence', () => {
  it.each([4000, 0])('displays the first refused proposal’s remaining %i paise', async remaining => {
    const harness = await open();
    await harness.shown('refund-approval-form');
    harness.set('#refund-approval-amount', '70.00');
    harness.set('#refund-approval-reason', 'Reviewed adjustment');
    harness.choose('Preserve');
    harness.find<HTMLButtonElement>('refund-approval-preview')!.click();
    (await harness.post('/approval-preview')).flush({ code: 'Refund.AmountExceedsRemaining', remainingAmountPaise: remaining },
      { status: 409, statusText: 'Conflict' });
    expect((await harness.shown('refund-approval-error')).textContent).toContain(remaining === 0 ? '₹0.00' : '₹40.00');
    expect(submitButton(harness)).toBeNull();
    harness.http.verify();
  });

  it.each(['reason', 'receiptVersion', 'orderId', 'confirmationId', 'remainingBeforePaise', 'remainingAfterPaise', 'effectiveAt'])(
    'retains the original key when recovered %s differs', async field => {
      const key = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
      const original = { operationKey: key, amountPaise: 4500, reason: 'Finance adjustment',
        treatment: 'EndAt', effectiveAt: '2028-02-10T04:30:00.000Z', expectedVersion: 0, impactDigest: preview.impactDigest, confirmed: true };
      const harness = await open({ pending: original });
      await harness.shown('refund-approval-pending');
      harness.find<HTMLButtonElement>('refund-approval-check')!.click();
      const values: Record<string, unknown> = { reason: 'Another reason', receiptVersion: 2, orderId: otherAccount,
        confirmationId: otherAccount, remainingBeforePaise: 10000, remainingAfterPaise: 1 };
      const at = field === 'effectiveAt' ? '2028-02-10T04:30:00.000001Z' : original.effectiveAt;
      (await harness.take('GET', '/approvals/operations/' + key)).flush(receipt(key, { reason: original.reason,
        access: { ...preview.access, treatment: original.treatment, effectiveAt: at, newPaidThrough: at, newAccessEndsAt: at },
        ...(field in values ? { [field]: values[field] } : {}) }));
      expect((await harness.shown('refund-approval-error')).textContent).toContain('outcome is unknown');
      expect(harness.find('refund-approval-receipt')).toBeNull();
      expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: original });
    });

  it('keeps the approval unresolved if a receipt names a different paid term', async () => {
    const harness = await open();
    await reviewed(harness);
    submitButton(harness)!.click();
    const sent = await harness.post('/approvals');
    sent.flush(receipt(sent.request.body.operationKey, { access: { ...preview.access, termId: otherAccount } }));
    expect((await harness.shown('refund-approval-error')).textContent).toContain('outcome is unknown');
    expect(harness.find('refund-approval-receipt')).toBeNull();
    expect(harness.records.get(harness.key)).toEqual({ revision: 0, pending: expect.objectContaining(sent.request.body) });
  });

  it('refuses a preview for another effective instant', async () => {
    const harness = await open();
    await harness.shown('refund-approval-form');
    harness.set('#refund-approval-amount', '45.00');
    harness.set('#refund-approval-reason', 'Reason');
    harness.choose('EndAt');
    harness.set('#refund-approval-effective', '2028-02-10T10:00:00');
    harness.find<HTMLButtonElement>('refund-approval-preview')!.click();
    const at = '2028-02-10T04:30:00.000001Z';
    (await harness.post('/approval-preview')).flush({ ...preview, access: { ...preview.access,
      treatment: 'EndAt', effectiveAt: at, newPaidThrough: at, newAccessEndsAt: at } });
    await harness.shown('refund-approval-error');
    expect(submitButton(harness)).toBeNull();
  });
});

it('recovers the original key while scoped intake is unavailable', async () => {
  const original = { operationKey: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', amountPaise: 4500,
    reason: 'Duplicate branch purchased before go-live', treatment: 'Preserve', effectiveAt: null,
    expectedVersion: 0, impactDigest: preview.impactDigest, confirmed: true, reviewedPayment: {
      orderId: item.orderId, confirmationId: item.confirmationId, verifiedAmountPaise: item.paymentAmountPaise,
      requestedAmountPaise: item.amountPaise, remainingAmountPaise: 12000 } };
  const harness = await open({ pending: original, unavailable: true });
  expect(harness.find('refund-approval-form')).toBeNull();
  harness.find<HTMLButtonElement>('refund-approval-check')!.click();
  (await harness.take('GET', '/approvals/operations/' + original.operationKey)).flush(receipt(original.operationKey));
  await harness.shown('refund-approval-receipt');
  expect(harness.records.get(harness.key)).toEqual({ revision: 1 });
  harness.http.verify();
});
