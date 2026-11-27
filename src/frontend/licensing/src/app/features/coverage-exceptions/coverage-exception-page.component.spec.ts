import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { CoverageExceptionDrafts } from './coverage-exception-drafts';
import { CoverageExceptionPageComponent } from './coverage-exception-page.component';
import type { ApproveCoverageExceptionCommand, CoverageExceptionApproval, CoverageExceptionPreview } from './coverage-exception-api.service';

const accountId = '33333333-3333-4333-8333-333333333333';
const exceptionId = '44444444-4444-4444-8444-444444444444';
const termId = '55555555-5555-4555-8555-555555555555';
const adminId = '66666666-6666-4666-8666-666666666666';
const northId = '11111111-1111-4111-8111-111111111111';
const southId = '22222222-2222-4222-8222-222222222222';
const hex = 'a'.repeat(64);

const samplePreview: CoverageExceptionPreview = {
  exceptionId, accountId, termId, proposalRevision: 1, sourceHash: hex, termsDigest: hex, grantScopeVersion: 0,
  outgoingShopId: northId, outgoingShopName: 'North', outgoingAffiliationId: northId,
  incomingShopId: southId, incomingShopName: 'South', incomingAffiliationId: southId,
  capacity: 3, memberAllowance: 5, normalRenewalAt: '2028-02-29T04:30:00Z',
  requestedEarlyStartAt: '2028-02-14T10:00:00Z', paidCutoverAt: '2028-02-14T14:00:00Z',
  overlapDisposition: 'CourtesyBridge', observedPermitDeadline: '2028-02-14T14:00:00Z',
  observedAt: '2028-02-14T10:00:00Z', lastContactAt: '2028-02-14T10:00:00Z', lastSynchronizedAt: null,
  observedPermitCount: 1, disconnectedQueuesUnknown: true,
  disconnectedQueuesNotice: 'Disconnected device queues are unknown.',
  billingOwnerUserId: adminId, billingOwnerRevision: 1, billingOwnerCurrent: true, billingOwnerAcknowledged: true,
  outgoingConsent: { leg: 'Outgoing', shopId: northId, shopName: 'North', status: 'Accepted', eventId: northId, actorId: northId, authorityVersion: 1, activeMembers: 1, recordedAt: '2028-02-14T10:00:00Z' },
  incomingConsent: { leg: 'Incoming', shopId: southId, shopName: 'South', status: 'Accepted', eventId: southId, actorId: southId, authorityVersion: 1, activeMembers: 1, recordedAt: '2028-02-14T10:00:00Z' },
  requestReason: 'Branch moving early', state: 'ReadyForReview', stateReason: null, canApprove: true, blockingReasons: [],
  courtesyGrantWillBeCreated: true, courtesyStartsAt: '2028-02-14T10:00:00Z', courtesyEndsAt: '2028-02-14T14:00:00Z',
  approval: null,
};

const sampleList = {
  accountId, evaluatedAt: '2028-02-14T10:00:00Z',
  items: [{
    exceptionId, accountId, termId, proposalRevision: 1, outgoingShopId: northId, outgoingShopName: 'North',
    incomingShopId: southId, incomingShopName: 'South', normalRenewalAt: '2028-02-29T04:30:00Z',
    requestedEarlyStartAt: '2028-02-14T10:00:00Z', paidCutoverAt: '2028-02-14T14:00:00Z',
    overlapDisposition: 'CourtesyBridge', observedPermitDeadline: '2028-02-14T14:00:00Z',
    state: 'ReadyForReview', outgoingConsentState: 'Accepted', incomingConsentState: 'Accepted',
    outgoingActivation: 'Pending', incomingActivation: 'Pending', courtesyActivation: 'Pending',
    courtesyGrantId: null, createdAt: '2028-02-14T10:00:00Z',
  }],
};

function makeApproval(opKey: string, state = 'Applied'): CoverageExceptionApproval {
  return {
    approvalId: '77777777-7777-4777-8777-777777777777', exceptionId, accountId, termId, generation: 1,
    operationKey: opKey, adminId, disposition: 'CourtesyBridge', reason: 'Verified branch move',
    decidedAt: '2028-02-14T10:01:00Z', state, outcomeReason: null, merchantPermitCount: 1,
    merchantMaxPermitEnd: '2028-02-14T14:00:00Z', courtesyGrantId: '88888888-8888-4888-8888-888888888888',
    courtesyTermId: '99999999-9999-4999-8999-999999999999', courtesyStartsAt: '2028-02-14T10:00:00Z',
    courtesyEndsAt: '2028-02-14T14:00:00Z', paidCutoverAt: '2028-02-14T14:00:00Z',
    normalRenewalAt: '2028-02-29T04:30:00Z', outgoingActivation: 'Acknowledged',
    incomingActivation: 'Waiting', courtesyActivation: 'Applying', appliedAt: '2028-02-14T10:01:05Z',
  };
}

class MemoryDrafts {
  readonly state = new Map<string, { revision: number; pending?: ApproveCoverageExceptionCommand }>();
  private k(s: { admin: string; account: string; order: string }) { return `${s.admin}:${s.account}:${s.order}`; }
  async read(s: { admin: string; account: string; order: string }) { return this.state.get(this.k(s)) ?? { revision: 0 }; }
  async claim(s: { admin: string; account: string; order: string }, rev: number, pending: ApproveCoverageExceptionCommand) {
    const cur = this.state.get(this.k(s));
    if (cur?.pending) return cur;
    const next = { revision: rev, pending };
    this.state.set(this.k(s), next);
    return next;
  }
  async settle(s: { admin: string; account: string; order: string }, key: string) {
    const cur = this.state.get(this.k(s));
    if (cur?.pending?.operationKey === key) this.state.set(this.k(s), { revision: cur.revision + 1 });
  }
}

async function flush(fixture: { detectChanges(): void }) {
  for (let i = 0; i < 4; i++) await Promise.resolve();
  fixture.detectChanges();
}

function mount(capabilities: string[] = ['licensing-manager'], drafts = new MemoryDrafts()) {
  const paramMap = new BehaviorSubject(convertToParamMap({ accountId }));
  const queryParamMap = new BehaviorSubject(convertToParamMap({ exception: exceptionId }));
  TestBed.configureTestingModule({
    imports: [CoverageExceptionPageComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: CoverageExceptionDrafts, useValue: drafts },
      { provide: AdminSessionService, useValue: { adminId: signal(adminId), sessionId: signal('sess-1'), capabilities: signal(capabilities) } },
      { provide: ActivatedRoute, useValue: { paramMap: paramMap.asObservable(), queryParamMap: queryParamMap.asObservable(),
        snapshot: { paramMap: paramMap.value, queryParamMap: queryParamMap.value } } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(CoverageExceptionPageComponent);
  fixture.detectChanges();
  return { fixture, http, drafts };
}

describe('CoverageExceptionPageComponent', () => {
  beforeEach(() => vi.restoreAllMocks());
  afterEach(() => TestBed.resetTestingModule());

  it('denies non-Licensing-Manager operators without loading requests', () => {
    const { fixture, http } = mount(['support']);
    expect(fixture.nativeElement.querySelector('[data-testid="exception-denied"]')).not.toBeNull();
    http.verify();
  });

  it('shows unknown disconnected queues, consents, separate courtesy scope and recovers a dropped approval by original key', async () => {
    const drafts = new MemoryDrafts();
    const { fixture, http } = mount(['licensing-manager'], drafts);

    http.expectOne(r => r.method === 'GET' && r.url.endsWith('/coverage-replacement-exceptions')).flush(sampleList);
    await flush(fixture);
    http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'csrf-1' });
    await flush(fixture);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/preview')).flush(samplePreview);
    await flush(fixture);

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="exception-unknown-queues"]')?.textContent).toContain('unknown');
    expect(el.querySelector('[data-testid="exception-disposition"]')?.textContent).toContain('Separate auditable courtesy grant');
    expect(el.querySelector('[data-testid="exception-outgoing-consent"]')?.textContent?.trim()).toBe('Accepted');
    expect(el.querySelector('[data-testid="exception-incoming-consent"]')?.textContent).toContain('Accepted');

    const reason = el.querySelector<HTMLTextAreaElement>('[data-testid="exception-reason"]')!;
    reason.value = 'Verified branch move';
    reason.dispatchEvent(new Event('input'));
    const check = el.querySelector<HTMLInputElement>('[data-testid="exception-confirm-check"]')!;
    check.checked = true;
    check.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('[data-testid="exception-approve"]')!.click();
    await flush(fixture);
    http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'csrf-2' });
    await flush(fixture);
    const approveReq = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/approve'));
    const frozen = approveReq.request.body as ApproveCoverageExceptionCommand;
    expect(frozen.confirmed).toBe(true);
    expect(frozen.acknowledgedPermitsAndUnknownQueues).toBe(true);
    expect(approveReq.request.headers.get('X-Admin-CSRF')).toBe('csrf-2');

    // Simulate lost response after server commit -> original key is preserved for recovery.
    approveReq.error(new ProgressEvent('error'));
    const checkBtn = await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="exception-pending"]')?.textContent).toContain(frozen.operationKey);
      const btn = el.querySelector<HTMLButtonElement>('[data-testid="exception-check"]')!;
      expect(btn.disabled).toBe(false);
      return btn;
    });

    // Recover using the preserved original key -> settles draft and displays distinct per-leg projection states.
    checkBtn.click();
    await flush(fixture);
    const recReq = http.expectOne(r => r.method === 'GET' && r.url.endsWith(`/operations/${frozen.operationKey}`));
    const approval = makeApproval(frozen.operationKey);
    recReq.flush(approval);
    await flush(fixture);
    http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'csrf-3' });
    await flush(fixture);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/preview')).flush({ ...samplePreview, canApprove: false, state: 'Applied', approval });
    await flush(fixture);
    http.expectOne(r => r.method === 'GET' && r.url.endsWith('/coverage-replacement-exceptions')).flush(sampleList);
    await flush(fixture);

    expect(el.querySelector('[data-testid="exception-pending"]')).toBeNull();
    expect(el.querySelector('[data-testid="exception-courtesy-grant"]')?.textContent).toContain(approval.courtesyGrantId);
    expect(el.querySelector('[data-testid="exception-leg-courtesy"]')?.textContent).toContain('Applying');
    expect(el.querySelector('[data-testid="exception-leg-outgoing"]')?.textContent).toContain('Acknowledged');
    http.verify();
  });

  it('preserves the frozen key and links to MFA sign-in when recent MFA has expired', async () => {
    const drafts = new MemoryDrafts();
    const { fixture, http } = mount(['licensing-manager'], drafts);

    http.expectOne(r => r.method === 'GET' && r.url.endsWith('/coverage-replacement-exceptions')).flush(sampleList);
    await flush(fixture);
    http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'csrf-1' });
    await flush(fixture);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/preview')).flush(samplePreview);
    await flush(fixture);

    const el: HTMLElement = fixture.nativeElement;
    const reason = el.querySelector<HTMLTextAreaElement>('[data-testid="exception-reason"]')!;
    reason.value = 'Verified branch move';
    reason.dispatchEvent(new Event('input'));
    const check = el.querySelector<HTMLInputElement>('[data-testid="exception-confirm-check"]')!;
    check.checked = true;
    check.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('[data-testid="exception-approve"]')!.click();
    await flush(fixture);
    http.expectOne('/api/admin/auth/sign-in/context').flush({ requestToken: 'csrf-2' });
    await flush(fixture);
    http.expectOne(r => r.method === 'POST' && r.url.endsWith('/approve'))
      .flush({ code: 'Coverage.ExceptionRecentMfaRequired' }, { status: 403, statusText: 'Forbidden' });
    await flush(fixture);

    expect(el.querySelector('[data-testid="exception-pending"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="exception-sign-in"]')).not.toBeNull();
    http.verify();
  });
});
