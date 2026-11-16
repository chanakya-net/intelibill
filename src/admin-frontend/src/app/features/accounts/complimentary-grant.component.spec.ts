import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { ComplimentaryGrantApi, type GrantPreview, type GrantResult, istInstant } from './complimentary-grant-api.service';
import { grantDrafts } from './complimentary-grant-drafts';
import { ComplimentaryGrantComponent } from './complimentary-grant.component';

const account = '33333333-3333-4333-8333-333333333333';
const shop = '11111111-1111-4111-8111-111111111111';
const preview: GrantPreview = {
  accountId: account, basis: 'Complimentary', shopIds: [shop], startsAt: '2027-02-28T18:30:00.000000Z',
  endsAt: '2027-03-31T18:30:00.000001Z', memberAllowance: 4, reason: 'Launch partner', timeZone: 'Asia/Kolkata',
  coverageMode: 'Independent', shopCapacity: 1, expectedVersion: 0, sourceHash: 'a'.repeat(64),
  evaluatedAt: '2027-03-01T00:00:00Z', paymentRecorded: false, graceEndsAt: null,
  shops: [{ shopId: shop, shopName: 'Fixture Shop', baselineState: 'Active', baselineBasis: 'Paid', baselineAccessEndsAt: '2027-03-08T04:30:00Z' }],
};
const granted: GrantResult = {
  operationKey: 'k', grantId: 'g', accountId: account, shopIds: [shop], startsAt: preview.startsAt, endsAt: preview.endsAt,
  memberAllowance: 4, reason: 'Launch partner', coverageMode: 'Independent', shopCapacity: 1, grantedVersion: 1,
  recordedAt: '2027-03-01T00:00:00Z', paymentRecorded: false, graceEndsAt: null,
};

function render(api: Partial<Record<keyof ComplimentaryGrantApi, ReturnType<typeof vi.fn>>>, admin = 'admin-1') {
  const adminId = signal<string | null>(admin);
  const sessionId = signal<string | null>('session-1');
  TestBed.configureTestingModule({
    providers: [
      { provide: AdminSessionService, useValue: { adminId, sessionId } },
      { provide: ComplimentaryGrantApi, useValue: api },
    ],
  });
  const view = TestBed.createComponent(ComplimentaryGrantComponent);
  view.componentRef.setInput('accountId', account);
  view.detectChanges();
  const page = view.nativeElement as HTMLElement;
  const type = (selector: string, value: string) => {
    const field = page.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    view.detectChanges();
  };
  const fill = () => {
    type('#grant-shops', shop);
    type('#grant-starts', '2027-03-01T00:00');
    type('#grant-ends', '2027-04-01T00:00:00');
    type('#grant-members', '4');
    type('#grant-reason', 'Launch partner');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    view.detectChanges();
  };
  const confirm = () => {
    const box = page.querySelector<HTMLInputElement>('.acknowledgement input')!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    view.detectChanges();
    [...page.querySelectorAll('button')].find(button => button.textContent?.includes('Confirm grant'))!.click();
    view.detectChanges();
  };
  return { view, page, type, fill, confirm, adminId, sessionId };
}

function memoryStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() { return items.size; },
    clear: () => items.clear(),
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => [...items.keys()][index] ?? null,
    removeItem: (key: string) => { items.delete(key); },
    setItem: (key: string, value: string) => { items.set(key, String(value)); },
  };
}

beforeEach(() => vi.stubGlobal('localStorage', memoryStorage()));
afterEach(() => vi.unstubAllGlobals());
afterEach(() => TestBed.resetTestingModule());

describe('complimentary grant editor', () => {
  it('previews exact IST facts with no payment or grace and sends the explicit IST instants', () => {
    const previewCall = vi.fn(() => of(preview));
    const { page, fill } = render({ preview: previewCall });
    fill();
    expect(previewCall).toHaveBeenCalledWith(account, {
      shopIds: [shop], startsAt: '2027-03-01T00:00:00+05:30', endsAt: '2027-04-01T00:00:00+05:30',
      memberAllowance: 4, reason: 'Launch partner',
    }, 'session-1');
    const text = page.querySelector('[data-testid="grant-preview"]')!.textContent!;
    expect(page.querySelector('[data-testid="grant-preview-ends"]')!.textContent).toContain('1 Apr 2027, 00:00:00.000001 IST');
    expect(page.querySelector('[data-testid="grant-preview-starts"]')!.textContent).toContain('1 Mar 2027, 00:00:00 IST');
    expect(text).toContain('None recorded; this is not revenue');
    expect(text).toContain('No grace period');
    expect(text).toContain('Existing access stays in effect first.');
  });

  it('invalidates the preview on any edit and refuses incomplete facts locally', () => {
    const previewCall = vi.fn(() => of(preview));
    const { page, fill, type } = render({ preview: previewCall });
    fill();
    type('#grant-members', '5');
    expect(page.querySelector('[data-testid="grant-preview"]')).toBeNull();
    type('#grant-ends', '');
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    expect(previewCall).toHaveBeenCalledTimes(1);
    expect(istInstant('2027-03-01')).toBeNull();
  });

  it('stores the exact command before sending and clears it once the grant is confirmed', () => {
    let stored: string | null = null;
    const confirmCall = vi.fn(() => {
      stored = localStorage.getItem(`intelibill.admin.complimentary-grant.v1.admin-1.${account}`);
      return of(granted);
    });
    const { page, fill, confirm } = render({ preview: vi.fn(() => of(preview)), confirm: confirmCall });
    fill();
    confirm();
    const command = (confirmCall.mock.calls[0] as unknown[])[1] as { operationKey: string };
    expect(JSON.parse(stored!).command).toEqual(command);
    expect(command).toMatchObject({ expectedVersion: 0, sourceHash: 'a'.repeat(64), confirmed: true, endsAt: preview.endsAt });
    expect(page.querySelector('[data-testid="grant-result"]')!.textContent).toContain('1 Apr 2027, 00:00:00.000001 IST');
    expect(grantDrafts.read('admin-1', account)).toBeNull();
  });

  it('refuses to send when the request cannot be stored durably', () => {
    const confirmCall = vi.fn(() => of(granted));
    const write = vi.spyOn(grantDrafts, 'write').mockReturnValue(false);
    const { page, fill, confirm } = render({ preview: vi.fn(() => of(preview)), confirm: confirmCall });
    fill();
    confirm();
    expect(confirmCall).not.toHaveBeenCalled();
    expect(page.querySelector('[data-testid="grant-error"]')!.textContent).toContain('was not sent');
    write.mockRestore();
  });

  it('keeps an unknown outcome with its original key across reload and recovers or retries only that operation', () => {
    const lost = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 0 })));
    const first = render({ preview: vi.fn(() => of(preview)), confirm: lost });
    first.fill();
    first.confirm();
    const key = ((lost.mock.calls[0] as unknown[])[1] as { operationKey: string }).operationKey;
    expect(first.page.querySelector('[data-testid="grant-pending-key"]')!.textContent).toBe(key);
    TestBed.resetTestingModule();

    const missing = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 })));
    const confirmAgain = vi.fn(() => of({ ...granted, operationKey: key }));
    const reloaded = render({ recover: missing, confirm: confirmAgain });
    expect(reloaded.page.querySelector('form')).toBeNull();
    [...reloaded.page.querySelectorAll('button')].find(button => button.textContent?.includes('Check grant outcome'))!.click();
    reloaded.view.detectChanges();
    expect(missing).toHaveBeenCalledWith(account, key, 'session-1');
    expect(reloaded.page.querySelector('[data-testid="grant-pending-key"]')!.textContent).toBe(key);
    [...reloaded.page.querySelectorAll('button')].find(button => button.textContent?.includes('Retry original grant'))!.click();
    reloaded.view.detectChanges();
    expect(((confirmAgain.mock.calls[0] as unknown[])[1] as { operationKey: string }).operationKey).toBe(key);
    expect(reloaded.page.querySelector('[data-testid="grant-result-key"]')!.textContent).toBe(key);
  });

  it('a definitive first refusal releases the request for a new preview; another admin never sees it', () => {
    const conflict = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'Grant.VersionConflict' } })));
    const { page, fill, confirm } = render({ preview: vi.fn(() => of(preview)), confirm: conflict });
    fill();
    confirm();
    expect(page.querySelector('[data-testid="grant-error"]')!.textContent).toContain('Preview again');
    expect(grantDrafts.read('admin-1', account)).toBeNull();
    grantDrafts.write({ adminId: 'admin-1', accountId: account, command: { ...preview, operationKey: 'x', confirmed: true } });
    TestBed.resetTestingModule();
    const other = render({}, 'admin-2');
    expect(other.page.querySelector('[data-testid="grant-pending"]')).toBeNull();
  });

  it('discards a late preview after the account changes', () => {
    const answers = new Subject<GrantPreview>();
    const { view, page, fill } = render({ preview: vi.fn(() => answers) });
    fill();
    view.componentRef.setInput('accountId', '55555555-5555-4555-8555-555555555555');
    view.detectChanges();
    answers.next(preview);
    view.detectChanges();
    expect(page.querySelector('[data-testid="grant-preview"]')).toBeNull();
    expect(page.querySelector<HTMLTextAreaElement>('#grant-shops')!.value).toBe('');
  });
});
