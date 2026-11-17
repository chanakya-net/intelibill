import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../../auth/admin-session.service';
import { PaymentReviewApi, type ReviewCommand, type ReviewDecision, type ReviewDetail } from './payment-review-api.service';
import { PaymentReviewDrafts } from './payment-review-drafts';
import { PaymentReviewPageComponent } from './payment-review-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const id = '22222222-2222-4222-8222-222222222222';
const other = '33333333-3333-4333-8333-333333333333';
const scope = { admin: 'finance', account, order: id };
const time = '2027-04-01T00:00:00Z';
const original: ReviewCommand = {
  operationKey: 'original', expectedVersion: 0, action: 'RecordNoAccessDecision',
  reason: 'Reviewed the recorded evidence', evidence: 'STATEMENT-FIXTURE', sourceHash: null,
};
const decision: ReviewDecision = {
  caseId: id, operationKey: original.operationKey, adminId: scope.admin, decidedAt: time,
  disposition: 'NoAccessDecisionRecorded', reason: original.reason, evidence: original.evidence, termId: null,
};
const detail: ReviewDetail = {
  case: { caseId: id, orderId: other, attemptId: null, manualReceiptId: null, inboxId: null,
    classification: 'CoverageChanged', disposition: null, expectedAmountPaise: 27000, expectedCurrency: 'INR',
    observedAmountPaise: 27000, observedCurrency: 'INR', providerId: 'development', receivingAccountId: 'fixture',
    environment: 'Development', configurationVersion: 'fixture/1', observedAt: time, capturedAt: time, createdAt: time },
  version: 0, decision: null, order: { planName: 'Multi-shop', period: 'Month', shopCount: 2 },
  shops: { items: [{ shopId: 'one', name: 'First shop' }, { shopId: 'two', name: 'Second shop' }], nextCursor: null },
  expectedReceivingAccount: 'fixture', expectedEnvironment: 'Development', expectedConfigurationVersion: 'fixture/1',
  recoverySourceHash: 'a'.repeat(64), recoveryBlocker: null, recoveryStartsAt: time, recoveryEndsAt: time,
  references: { expectedOrder: null, observedOrder: null, observedPayment: null,
    verifiedAmountPaise: null, verifiedCurrency: null, verifiedAt: null },
};

function setup({ pending, billing = true, view = detail }: {
  pending?: ReviewCommand;
  billing?: boolean;
  view?: ReviewDetail;
} = {}) {
  const route = new BehaviorSubject(convertToParamMap({ account, case: id }));
  const adminId = signal(scope.admin);
  const sessionId = signal('session');
  const capabilities = signal(billing ? ['billing'] : []);
  const api = {
    read: vi.fn(async (): Promise<ReviewDetail> => view),
    decide: vi.fn(async (): Promise<ReviewDecision> => decision),
    recover: vi.fn(async (): Promise<ReviewDecision> => decision),
  };
  const drafts = {
    read: vi.fn(async (): Promise<{ revision: number; pending?: ReviewCommand }> => ({ revision: 0, pending })),
    claim: vi.fn(async (_scope: typeof scope, revision: number, command: ReviewCommand) => ({ revision, pending: command })),
    settle: vi.fn(async () => {}),
  };
  TestBed.configureTestingModule({ providers: [
    provideRouter([]),
    { provide: ActivatedRoute, useValue: { queryParamMap: route } },
    { provide: AdminSessionService, useValue: { adminId, sessionId, capabilities } },
    { provide: PaymentReviewApi, useValue: api },
    { provide: PaymentReviewDrafts, useValue: drafts },
  ] });
  const fixture = TestBed.createComponent(PaymentReviewPageComponent);
  const page = fixture.nativeElement as HTMLElement;
  const update = () => fixture.detectChanges();
  const button = (label: string) => Array.from(page.querySelectorAll('button')).find(el => el.textContent?.trim() === label);
  const ready = (label: string) => vi.waitFor(() => {
    update();
    expect(button(label)?.disabled).toBe(false);
  });
  const alert = (message: string) => vi.waitFor(() => {
    update();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(message);
  });
  fixture.detectChanges();
  return { api, drafts, page, route, adminId, sessionId, update, button, ready, alert };
}

const retry = 'Retry original decision';
const submit = 'Record finance decision';
const reject = (code: string) => new HttpErrorResponse({ status: 409, error: { code } });

afterEach(() => TestBed.resetTestingModule());

describe('payment review', () => {
  it('requires Billing before loading evidence', () => {
    const { api, drafts, page } = setup({ billing: false });
    expect(page.textContent).toContain('Billing access is required');
    expect(api.read).not.toHaveBeenCalled();
    expect(drafts.read).not.toHaveBeenCalled();
  });

  it.each(['PaymentReview.CaseVersionConflict', 'PaymentReview.RecoveryUnavailable', 'PaymentReview.InvalidDecision'])(
    'settles and reloads the original after %s', async code => {
      const { api, drafts, button, ready, alert } = setup({ pending: original });
      await ready(retry);
      api.decide.mockRejectedValueOnce(reject(code));
      drafts.read.mockResolvedValueOnce({ revision: 1 });
      button(retry)!.click();
      await alert('The decision was declined');
      expect(drafts.settle).toHaveBeenCalledExactlyOnceWith(scope, original.operationKey);
      expect(api.read).toHaveBeenCalledTimes(2);
      expect(button(submit)?.disabled).toBe(false);
    },
  );

  it('keeps the original decision when MFA expires and points to a scoped sign-in', async () => {
    const { api, drafts, page, button, ready, alert } = setup({ pending: original });
    await ready(retry);
    api.decide.mockRejectedValueOnce(reject('ManualPayment.RecentMfaRequired'));
    button(retry)!.click();
    await alert('Sign in with MFA again');
    const link = Array.from(page.querySelectorAll('a')).find(el => el.textContent?.trim() === 'Sign in with MFA');
    expect(link?.getAttribute('href')).toBe('/auth/sign-in?reviewAccount=' + account + '&reviewCase=' + id);
    expect(drafts.settle).not.toHaveBeenCalled();
    expect(page.querySelector('[data-testid="review-operation"]')?.textContent).toContain(original.operationKey);
  });

  it('sends no decision if the durable claim fails', async () => {
    const { api, drafts, page, ready, alert } = setup();
    await ready(submit);
    drafts.claim.mockRejectedValueOnce(new Error('Storage unavailable'));
    page.querySelector<HTMLSelectElement>('#review-action')!.value = original.action;
    page.querySelector<HTMLTextAreaElement>('#review-reason')!.value = original.reason;
    page.querySelector<HTMLInputElement>('#review-evidence')!.value = original.evidence;
    page.querySelector('form:has(#review-action)')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await alert('Payment review is unavailable');
    expect(drafts.claim).toHaveBeenCalledOnce();
    expect(api.decide).not.toHaveBeenCalled();
  });

  it.each(['account', 'case', 'admin', 'session'] as const)('discards a result from the previous %s scope', async key => {
    const { api, drafts, page, route, adminId, sessionId, update, button, ready } = setup({ pending: original });
    await ready(retry);
    let complete!: (value: ReviewDecision) => void;
    const response = new Promise<ReviewDecision>(resolve => { complete = resolve; });
    api.decide.mockReturnValueOnce(response);
    button(retry)!.click();
    drafts.read.mockResolvedValueOnce({ revision: 0 });
    if (key === 'admin') adminId.set('other-finance');
    else if (key === 'session') sessionId.set('other-session');
    else route.next(convertToParamMap({ account, case: id, [key]: other }));
    update();
    await ready(submit);
    complete(decision);
    await response;
    await Promise.resolve();
    update();
    expect(api.read).toHaveBeenCalledTimes(2);
    expect(page.querySelector('[data-testid="review-result"]')).toBeNull();
    expect(page.querySelector('[data-testid="review-operation"]')).toBeNull();
  });

  it.each(['caseId', 'adminId', 'operationKey'] as const)(
    'keeps the original draft when the returned %s differs', async field => {
      const { api, drafts, page, button, ready, alert } = setup({ pending: original });
      await ready(retry);
      api.recover.mockResolvedValueOnce({ ...decision, [field]: 'different' });
      button('Check original decision')!.click();
      await alert('Decision is unresolved');
      expect(drafts.settle).not.toHaveBeenCalled();
      expect(api.read).toHaveBeenCalledOnce();
      expect(page.querySelector('[data-testid="review-operation"]')?.textContent).toContain(original.operationKey);
    },
  );

  it.each([
    { hash: null, paged: false, available: false },
    { hash: detail.recoverySourceHash, paged: true, available: false },
    { hash: detail.recoverySourceHash, paged: false, available: true },
  ])('requires both recovery proof and the complete shop list ($paged, $available)', async ({ hash, paged, available }) => {
    const view = { ...detail, recoverySourceHash: hash, shops: paged
      ? { items: detail.shops.items.slice(0, 1), nextCursor: 'next' } : detail.shops };
    const { page, ready, api, button } = setup({ view });
    await ready(submit);
    const recovery = () => page.querySelector('option[value="RecoverAcceptedTerms"]');
    expect(Boolean(recovery())).toBe(available);
    if (paged) {
      api.read.mockResolvedValueOnce({ ...detail, shops: { items: detail.shops.items.slice(1), nextCursor: null } });
      button('Load more shops')!.click();
      await ready(submit);
      expect(recovery()).not.toBeNull();
      expect(api.read).toHaveBeenLastCalledWith(account, id, 'session', 'next');
    }
  });
});
