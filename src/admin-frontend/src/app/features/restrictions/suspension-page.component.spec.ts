import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { SuspensionApi, type ReleaseImpact, type SuspensionCommand, type SuspensionImpact, type SuspensionStatus } from './suspension-api.service';
import { SuspensionDrafts, type PendingSuspension } from './suspension-drafts';
import { SuspensionPageComponent } from './suspension-page.component';

const account = 'a0000000-0000-4000-8000-000000000000';
const shopId = 'b0000000-0000-4000-8000-000000000000';
const session = 'admin-session';
const savedRestriction: SuspensionStatus = {
  id: 'f0000000-0000-4000-8000-000000000000', shopId, mode: 'Security',
  effectiveAt: '2026-12-01T10:00:00Z', recordedAt: '2026-12-01T10:00:00Z',
  customerExplanation: 'Contact support.', propagation: 'Acknowledged', shops: [],
};
const releaseImpact: ReleaseImpact = {
  restrictionId: savedRestriction.id, version: 1, evaluatedAt: savedRestriction.effectiveAt,
  impactDigest: 'b'.repeat(64), shops: [{ shopId, shopName: 'Fixture shop', state: 'Expired',
    basis: null, paidThrough: null, graceEndsAt: null, accessEndsAt: null,
    survivingSuspension: { mode: 'Trading', effectiveAt: savedRestriction.effectiveAt } }],
};

function render(capabilities = ['security-administrator'], pending: PendingSuspension | null = null) {
  const impact: SuspensionImpact = {
    accountId: account, accountName: 'Fixture account', selection: { scope: 'Shop', shopId },
    version: 0, alreadySuspended: false, impactDigest: 'a'.repeat(64),
    shops: [{ shopId, shopName: 'Fixture shop', coverageMode: 'Independent' }],
  };
  const preview = vi.fn(() => of({ ...impact, selection: { ...impact.selection, isValid: true } }));
  const suspend = vi.fn();
  const list = vi.fn(() => of([savedRestriction]));
  const previewRelease = vi.fn<SuspensionApi['previewRelease']>(() => of(releaseImpact));
  const release = vi.fn(() => of({ ...savedRestriction, release: { propagation: 'Pending', shops: [],
    releasedAt: savedRestriction.effectiveAt } }));
  const sessionId = signal(session);
  const claim = vi.fn(async (draft: PendingSuspension) => draft);
  TestBed.configureTestingModule({ providers: [
    provideRouter([]),
    { provide: AdminSessionService, useValue: { capabilities: signal(capabilities), adminId: signal('admin'), sessionId } },
    { provide: SuspensionApi, useValue: { preview, suspend, list, previewRelease, release } },
    { provide: SuspensionDrafts, useValue: { read: async () => pending, claim, clear: async () => {} } },
  ] });
  const fixture = TestBed.createComponent(SuspensionPageComponent);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  const set = (id: string, value: string, event = 'input') => {
    const control = page.querySelector<HTMLInputElement>(`#restriction-${id}`)!;
    control.value = value;
    control.dispatchEvent(new Event(event));
    fixture.detectChanges();
  };
  const button = (name: string) => [...page.querySelectorAll('button')]
    .find(button => button.textContent?.trim() === name);
  const click = (name: string) => button(name)?.click();
  const settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return { fixture, page, set, button, click, preview, suspend, sessionId, settle, claim, release, list, previewRelease };
}

afterEach(() => TestBed.resetTestingModule());

describe('restriction editor', () => {
  it('requires deliberate shop selection and clears the preview when switching to account scope', async () => {
    const view = render();
    view.set('account', account);
    view.click('Preview impact');
    await view.settle();
    expect(view.preview).not.toHaveBeenCalled();
    view.set('scope', 'Shop', 'change');
    view.set('shop', shopId);
    view.click('Preview impact');
    await view.settle();
    expect(view.preview).toHaveBeenCalledWith(account, { scope: 'Shop', shopId }, session);
    expect(view.page.textContent).toContain('Fixture shop');
    view.set('scope', 'Account', 'change');
    expect(view.page.textContent).not.toContain('Fixture shop');
  });

  it('saves only declared selection fields from the preview', async () => {
    const view = render();
    view.set('account', account);
    view.set('scope', 'Shop', 'change');
    view.set('shop', shopId);
    view.click('Preview impact');
    await view.settle();
    view.set('mode', 'Trading', 'change');
    view.set('customer', 'Customer explanation');
    view.set('internal', 'Internal evidence');
    view.set('reference', 'SUP-78');
    view.click('Apply suspension');
    await view.settle();
    expect(view.claim.mock.calls[0]?.[0].command).not.toHaveProperty('isValid');
  });

  it.each(['Account', 'Shop'] as const)('shows the stored %s target when the selector names the other scope', async scope => {
    const command: SuspensionCommand = {
      scope, shopId: scope === 'Shop' ? shopId : null, mode: 'Security',
      effectiveAt: '2026-12-01T10:00:00Z', operationKey: account,
      customerExplanation: 'Customer explanation', internalReason: 'Internal evidence',
      reviewReference: 'SUP-78', expectedVersion: 0, impactDigest: 'a'.repeat(64),
    };
    const view = render(undefined, { adminId: 'admin', accountId: account, command });
    view.set('account', account);
    view.set('scope', scope === 'Shop' ? 'Account' : 'Shop', 'change');
    if (scope === 'Account') view.set('shop', shopId);
    view.click('Preview impact');
    await view.settle();
    const draft = view.page.querySelector('[data-testid="pending-suspension"]')?.textContent;
    expect(draft).toContain(scope === 'Shop' ? shopId : 'Entire billing account');
    expect(draft).toContain('Security');
    expect(draft).toContain('15:30');
  });

  it('previews remaining access and submits only after the reasoned confirmation', async () => {
    const view = render();
    view.set('account', account);
    view.click('Refresh saved restrictions');
    await view.settle();
    view.click('Preview release');
    await view.settle();
    expect(view.page.textContent).toContain('Expired');
    expect(view.page.textContent).toContain('Trading suspension remains');
    expect(view.release).not.toHaveBeenCalled();
    view.set('release-reason', 'Review completed.');
    view.click('Confirm release');
    await view.settle();
    expect(view.release).toHaveBeenCalledWith(account, savedRestriction.id,
      expect.objectContaining({ expectedVersion: 1, reason: 'Review completed.' }), session);
    expect(view.page.textContent).toContain('Pending merchant acknowledgement');
  });

  it.each(['account', 'session'])('discards a release preview after the %s changes', async changed => {
    const view = render();
    const response = new Subject<ReleaseImpact>();
    view.previewRelease.mockReturnValueOnce(response);
    view.set('account', account);
    view.click('Refresh saved restrictions');
    await view.settle();
    view.click('Preview release');
    await view.settle();
    expect(view.previewRelease).toHaveBeenCalledWith(account, savedRestriction.id, session);

    if (changed === 'account') {
      view.set('account', shopId);
      view.set('account', account);
    } else {
      view.sessionId.set('replacement-session');
      view.fixture.detectChanges();
      view.set('account', account);
    }
    response.next(releaseImpact);
    await vi.waitFor(async () => {
      await view.settle();
      expect(view.button('Refresh saved restrictions')?.disabled).toBe(false);
    });
    view.click('Refresh saved restrictions');
    await view.settle();
    expect(view.page.textContent).toContain('Preview release');
    expect(view.page.textContent).not.toContain('Access after release');
    expect(view.page.textContent).not.toContain('Confirm release');
  });

  it('shows active and released restrictions without write controls for support', async () => {
    const view = render(['support']);
    view.list.mockReturnValueOnce(of([savedRestriction, {
      ...savedRestriction, id: account, release: {
        releasedBy: session, releasedAt: savedRestriction.effectiveAt,
        propagation: 'Pending', shops: [],
      },
    }]));
    view.set('account', account);
    view.click('Refresh saved restrictions');
    await view.settle();
    expect(view.page.textContent).toContain('Contact support.');
    expect(view.page.querySelector('[data-testid="release-propagation"]')?.textContent)
      .toContain('Pending merchant acknowledgement');
    expect(view.page.querySelector('#restriction-scope')).toBeNull();
    expect(view.page.textContent).not.toContain('Preview release');
    expect(view.page.textContent).not.toContain('Confirm release');
  });

  it('clears reviewed details when the verified session changes', async () => {
    const view = render();
    view.set('account', account);
    view.set('scope', 'Shop', 'change');
    view.set('shop', shopId);
    view.click('Preview impact');
    await view.settle();
    view.set('internal', 'Private fixture reason');
    view.sessionId.set('replacement-session');
    view.fixture.detectChanges();
    expect(view.page.querySelector<HTMLInputElement>('#restriction-account')?.value).toBe('');
    expect(view.page.querySelector('#restriction-internal')).toBeNull();
  });
});
