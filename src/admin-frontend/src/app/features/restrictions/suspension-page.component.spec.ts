import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { SuspensionApi, type SuspensionCommand, type SuspensionImpact } from './suspension-api.service';
import { SuspensionDrafts, type PendingSuspension } from './suspension-drafts';
import { SuspensionPageComponent } from './suspension-page.component';

const account = 'a0000000-0000-4000-8000-000000000000';
const shopId = 'b0000000-0000-4000-8000-000000000000';
const session = 'admin-session';

function render(capabilities = ['security-administrator'], pending: PendingSuspension | null = null) {
  const impact: SuspensionImpact = {
    accountId: account, accountName: 'Fixture account', selection: { scope: 'Shop', shopId },
    version: 0, alreadySuspended: false, impactDigest: 'a'.repeat(64),
    shops: [{ shopId, shopName: 'Fixture shop', coverageMode: 'Independent' }],
  };
  const preview = vi.fn(() => of({ ...impact, selection: { ...impact.selection, isValid: true } }));
  const suspend = vi.fn();
  const sessionId = signal(session);
  const claim = vi.fn(async (draft: PendingSuspension) => draft);
  TestBed.configureTestingModule({ providers: [
    provideRouter([]),
    { provide: AdminSessionService, useValue: { capabilities: signal(capabilities), adminId: signal('admin'), sessionId } },
    { provide: SuspensionApi, useValue: { preview, suspend } },
    { provide: SuspensionDrafts, useValue: { read: async () => pending, claim } },
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
  const click = (name: string) => [...page.querySelectorAll('button')]
    .find(button => button.textContent?.trim() === name)?.click();
  const settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return { fixture, page, set, click, preview, suspend, sessionId, settle, claim };
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

  it('offers status without a write form for support', () => {
    const { page } = render(['support']);
    expect(page.textContent).toContain('Refresh saved restrictions');
    expect(page.querySelector('#restriction-scope')).toBeNull();
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
