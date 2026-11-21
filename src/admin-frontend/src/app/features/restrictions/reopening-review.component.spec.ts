import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { ReopeningApi, type ReopeningImpact } from './reopening-api.service';
import { ReopeningDrafts, type PendingReopening } from './reopening-drafts';
import { ReopeningReviewComponent } from './reopening-review.component';

const account = 'a0000000-0000-4000-8000-000000000000';
const closureId = 'b0000000-0000-4000-8000-000000000000';
const shopId = 'c0000000-0000-4000-8000-000000000000';
const ownerId = 'd0000000-0000-4000-8000-000000000000';
const at = '2026-11-21T04:00:00Z';

async function render(capabilities = ['licensing-manager'], coverageAdequate = true) {
  const impact: ReopeningImpact = {
    closureId, shopId, shopName: 'Lake Shop', closedAt: at, version: 1, impactDigest: 'a'.repeat(64),
    coverage: null, coverageAdequate, otherActiveClosures: 0, survivingSuspension: null,
  };
  const preview = vi.fn(() => of(impact));
  const save = vi.fn(() => of({ id: closureId, closureId, shopId, reopenedAt: at }));
  const list = vi.fn(() => of([{ id: closureId, scope: 'Shop', savedAt: at, shops: [{ shopId, shopName: 'Lake Shop' }] }]));
  const claim = vi.fn(async (draft: PendingReopening) => draft);
  TestBed.configureTestingModule({ providers: [
    provideRouter([]),
    { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
    { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ account }) } } },
    { provide: AdminSessionService, useValue: {
      state: signal('authenticated'), capabilities: signal(capabilities), adminId: signal('admin'), sessionId: signal('session'),
    } },
    { provide: ReopeningApi, useValue: { list, preview, save } },
    { provide: ReopeningDrafts, useValue: { read: async () => null, claim, clear: async () => {} } },
  ] });
  const fixture = TestBed.createComponent(ReopeningReviewComponent);
  const page: HTMLElement = fixture.nativeElement;
  const update = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const input = (id: string, value: string) => {
    const field = page.querySelector<HTMLInputElement>(`#reopening-${id}`)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const button = (text: string) => [...page.querySelectorAll('button')].find(item => item.textContent?.trim() === text);
  await update();
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(page.textContent).toContain('Lake Shop');
  });
  const until = (assert: () => void) => vi.waitFor(() => {
    fixture.detectChanges();
    assert();
  });
  return { page, preview, save, claim, input, button, update, until };
}

afterEach(() => TestBed.resetTestingModule());

describe('shop reopening review', () => {
  it('requires explicit consent evidence and stores the exact scoped operation before sending', async () => {
    const view = await render();
    await view.until(() => expect(view.button('Review reopening')?.disabled).toBe(false));
    view.button('Review reopening')!.click();
    await view.until(() => expect(view.button('Confirm reopening')).toBeDefined());
    const confirm = view.button('Confirm reopening')!;
    expect(confirm.disabled).toBe(true);
    view.input('reason', 'Coverage reviewed');
    view.input('owner', ownerId);
    view.input('consent-at', '2026-11-21T10:00');
    view.input('reference', 'REVIEW-898');
    view.input('evidence', 'The verified owner requested reopening of Lake Shop.');
    expect(confirm.disabled).toBe(true);
    view.page.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    await view.update();
    expect(confirm.disabled).toBe(false);
    confirm.click();
    await view.until(() => expect(view.save).toHaveBeenCalledOnce());
    expect(view.claim).toHaveBeenCalledOnce();
    const draft = view.claim.mock.calls[0][0];
    expect(draft).toMatchObject({ accountId: account, closureId, shopId, command: {
      ownerConsentConfirmed: true, ownerConsentUserId: ownerId, ownerConsentAt: '2026-11-21T04:30:00.000Z',
      ownerConsentReference: 'REVIEW-898', expectedVersion: 1, impactDigest: 'a'.repeat(64),
    } });
    expect(view.save).toHaveBeenCalledWith(draft, draft.command, 'session');
    expect(view.claim.mock.invocationCallOrder[0]).toBeLessThan(view.save.mock.invocationCallOrder[0]);
  });

  it('shows the coverage refusal and only offers review to Licensing Managers', async () => {
    const manager = await render(['licensing-manager'], false);
    await manager.until(() => expect(manager.button('Review reopening')?.disabled).toBe(false));
    manager.button('Review reopening')!.click();
    await manager.until(() => expect(manager.page.textContent).toContain('Reopening requires active coverage other than a trial.'));
    expect(manager.button('Confirm reopening')).toBeUndefined();
    TestBed.resetTestingModule();

    const support = await render(['support']);
    expect(support.page.textContent).toContain('Lake Shop');
    expect(support.button('Review reopening')).toBeUndefined();
  });
});
