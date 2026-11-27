import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { TrialExtensionApi, exactIstInstant, type TrialExtensionPreview } from './trial-extension-api.service';
import { TrialExtensionDrafts, type PendingTrialExtension } from './trial-extension-drafts';
import { TrialExtensionComponent } from './trial-extension.component';

const account = 'account-1';
const shop = 'shop-1';
const adminId = 'admin-1';
const sessionId = 'session-1';
const preview: TrialExtensionPreview = {
  shopId: shop, shopName: 'Fixture Shop', startsAt: '2027-02-10T00:00:00.123456Z',
  originalEndsAt: '2027-02-24T00:00:00.123456Z', currentEndsAt: '2027-02-24T00:00:00.123456Z',
  endsAt: '2027-03-10T13:15:30.123456Z', reason: 'Inventory count', reviewReference: 'EVAL-857',
  currentVersion: 0, sourceHash: 'a'.repeat(64), accountTrialCap: 1, accountTrialCapReached: true,
};

function render(api: Partial<TrialExtensionApi>, drafts: Partial<TrialExtensionDrafts> = {}) {
  TestBed.configureTestingModule({
    providers: [
      { provide: TrialExtensionApi, useValue: api },
      { provide: TrialExtensionDrafts, useValue: { read: async () => null, clear: async () => {}, ...drafts } },
      { provide: AdminSessionService, useValue: { adminId: signal(adminId), sessionId: signal(sessionId) } },
    ],
  });
  const fixture = TestBed.createComponent(TrialExtensionComponent);
  fixture.componentRef.setInput('accountId', account);
  fixture.componentRef.setInput('trials', [{ shopId: shop, shopName: preview.shopName }]);
  const page = fixture.nativeElement as HTMLElement;
  const renderChanges = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const type = (selector: string, value: string, event = 'input') => {
    const field = page.querySelector<HTMLInputElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event(event));
    fixture.detectChanges();
  };
  const click = (name: string) => {
    const button = [...page.querySelectorAll('button')].find(item => item.textContent?.trim() === name)!;
    button.click();
  };
  const prepare = async () => {
    await renderChanges();
    type('#extension-shop', shop, 'change');
    type('#extension-end', '2027-03-10 18:45:30.123456');
    type('#extension-reason', preview.reason);
    type('#extension-reference', preview.reviewReference);
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await renderChanges();
  };
  return { page, type, click, prepare, renderChanges };
}

afterEach(() => TestBed.resetTestingModule());

describe('trial extension', () => {
  it('sends an explicit IST instant, shows the exact reviewed facts and invalidates the preview on edit', async () => {
    const request = vi.fn(() => of(preview));
    const view = render({ preview: request });
    await view.prepare();
    expect(request).toHaveBeenCalledWith(account, shop, {
      endsAt: '2027-03-10T18:45:30.123456+05:30', reason: preview.reason, reviewReference: preview.reviewReference,
    }, sessionId);
    const panel = view.page.querySelector('[data-testid="trial-extension-preview"]')!;
    expect(panel.textContent).toContain('10 Mar 2027, 18:45:30.123456 IST');
    expect(panel.textContent).toContain(preview.reviewReference);
    expect(panel.textContent).toContain('exceeds the account’s cap of 1');
    view.type('#extension-reference', 'EVAL-858');
    expect(view.page.querySelector('[data-testid="trial-extension-preview"]')).toBeNull();
    expect(exactIstInstant('2027-03-10T18:45:30.1234567')).toBeNull();
  });

  it('drops a preview response when the selected facts change while it is loading', async () => {
    const late = new Subject<TrialExtensionPreview>();
    const view = render({ preview: () => late });
    await view.prepare();
    view.type('#extension-reason', 'Different reason');
    late.next(preview);
    await view.renderChanges();
    expect(view.page.querySelector('[data-testid="trial-extension-preview"]')).toBeNull();
  });

  it('retains the original operation across an unknown outcome and lookup 404', async () => {
    let saved: PendingTrialExtension | null = null;
    const failed = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 0 })));
    const drafts = {
      read: async () => saved,
      claim: async (pending: PendingTrialExtension) => { saved = pending; return pending; },
    };
    const first = render({ preview: () => of(preview), confirm: failed }, drafts);
    await first.prepare();
    const approval = first.page.querySelector<HTMLInputElement>('[type=checkbox]')!;
    approval.checked = true;
    approval.dispatchEvent(new Event('change'));
    await first.renderChanges();
    first.click('Approve extension');
    await first.renderChanges();
    expect(saved).toBeTruthy();
    const pending = saved as unknown as PendingTrialExtension;
    expect(failed).toHaveBeenCalledWith(account, shop, pending.command, sessionId);

    TestBed.resetTestingModule();
    const missing = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 })));
    const again = render({ recover: missing, confirm: failed }, drafts);
    await again.renderChanges();
    expect(again.page.querySelector('form')).toBeNull();
    again.click('Check extension outcome');
    await again.renderChanges();
    expect(missing).toHaveBeenCalledWith(account, shop, pending.command.operationKey, sessionId);
    again.click('Retry original extension');
    await again.renderChanges();
    expect(failed).toHaveBeenCalledTimes(2);
    expect(failed).toHaveBeenLastCalledWith(account, shop, pending.command, sessionId);
  });
});
