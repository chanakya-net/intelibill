import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { AuthService } from '../../../core/auth/auth.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { BillingAccountDraftStore } from '../services/billing-account-draft.store';
import { BillingAccount } from '../services/billing-account.models';
import { BILLING_ACCOUNT_ENDPOINTS } from '../services/billing-account.service';
import { BillingAccountsPageComponent } from './billing-accounts-page.component';

describe('BillingAccountsPageComponent', () => {
  const url = BILLING_ACCOUNT_ENDPOINTS.accounts;
  const owner = 'user-a';
  const account: BillingAccount = {
    accountId: '4b3a2a46-5f3b-8c1f-9d64-6b0a2b1c3d4e',
    name: 'Acme Billing',
    role: 'Owner',
    createdAt: '2026-11-15T10:00:00+00:00',
  };
  let fixture: ComponentFixture<BillingAccountsPageComponent>;
  let http: HttpTestingController;
  let drafts: BillingAccountDraftStore;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [BillingAccountsPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { session: signal({ user: { id: owner } }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    drafts = TestBed.inject(BillingAccountDraftStore);
  }

  function render(accounts: readonly BillingAccount[] = []): HTMLElement {
    fixture = TestBed.createComponent(BillingAccountsPageComponent);
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url }).flush({ accounts });
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function query<T extends Element>(root: HTMLElement, selector: string): T | null {
    return root.querySelector<T>(selector);
  }

  function submit(root: HTMLElement, name?: string): void {
    const input = query<HTMLInputElement>(root, '[data-testid="billing-account-name"]')!;
    if (name !== undefined) {
      input.value = name;
      input.dispatchEvent(new Event('input'));
    }
    query<HTMLFormElement>(root, '[data-testid="billing-account-form"]')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function open(accounts: readonly BillingAccount[] = []): ComponentFixture<BillingAccountsPageComponent> {
    const instance = TestBed.createComponent(BillingAccountsPageComponent);
    instance.detectChanges();
    http.expectOne({ method: 'GET', url }).flush({ accounts });
    instance.detectChanges();
    return instance;
  }

  function submitIn(instance: ComponentFixture<BillingAccountsPageComponent>, name?: string): void {
    fixture = instance;
    submit(instance.nativeElement as HTMLElement, name);
  }

  function posted(name: string): TestRequest {
    return http.expectOne((req) => req.method === 'POST' && (req.body as { name: string }).name === name);
  }

  function rows(instance: ComponentFixture<BillingAccountsPageComponent>): number {
    instance.detectChanges();
    return (instance.nativeElement as HTMLElement).querySelectorAll('[data-testid="billing-account-row"]').length;
  }

  function nameInput(instance: ComponentFixture<BillingAccountsPageComponent>): HTMLInputElement {
    instance.detectChanges();
    return query<HTMLInputElement>(instance.nativeElement as HTMLElement, '[data-testid="billing-account-name"]')!;
  }

  const second: BillingAccount = { ...account, accountId: '9c1d2e3f-4a5b-8c6d-9e7f-0a1b2c3d4e5f', name: 'Bravo' };
  const lost = new ProgressEvent('error');

  beforeEach(() => {
    stubMemoryStorage();
    setup();
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
  });

  it('shows the empty state and creates nothing on load', () => {
    const root = render();

    expect(query(root, '[data-testid="billing-accounts-empty"]')).not.toBeNull();
    http.expectNone({ method: 'POST', url });
  });

  it('lists each account with its role', () => {
    const root = render([account]);

    const rows = root.querySelectorAll('[data-testid="billing-account-row"]');
    expect(rows).toHaveLength(1);
    expect(rows[0].textContent).toContain('Acme Billing');
    expect(rows[0].textContent).toContain('billing.roles.Owner');
  });

  it('creates explicitly with a key frozen before the request', () => {
    const root = render();
    submit(root, '  Acme Billing  ');

    const request = http.expectOne({ method: 'POST', url });
    const body = request.request.body as { operationKey: string; name: string };
    expect(body.name).toBe('Acme Billing');
    expect(drafts.load(owner)).toEqual({ ownerUserId: owner, operationKey: body.operationKey, name: 'Acme Billing' });
    submit(root);
    http.expectNone({ method: 'POST', url });

    request.flush(account, { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="billing-account-row"]')).toHaveLength(1);
    http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
    expect(drafts.load(owner)).toBeNull();
  });

  it('retries an unfinished attempt with the same key and frozen name', () => {
    const root = render();
    submit(root, 'Acme Billing');
    const first = http.expectOne({ method: 'POST', url });
    const key = (first.request.body as { operationKey: string }).operationKey;
    first.flush({ code: 'Billing.DelegationUnavailable' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    const input = query<HTMLInputElement>(root, '[data-testid="billing-account-name"]')!;
    expect(input.readOnly).toBe(true);
    expect(query(root, '[data-testid="billing-account-pending"]')).not.toBeNull();
    submit(root, 'Changed name');

    const retry = http.expectOne({ method: 'POST', url });
    expect(retry.request.body).toEqual({ operationKey: key, name: 'Acme Billing' });
    retry.flush(account, { status: 200, statusText: 'OK' });
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="billing-account-row"]')).toHaveLength(1);
    http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
    expect(drafts.load(owner)).toBeNull();
  });

  it('restores a pending attempt after reload and resubmits it unchanged', () => {
    drafts.save({ ownerUserId: owner, operationKey: 'frozen-key', name: 'Frozen name' });
    const root = render();

    const input = query<HTMLInputElement>(root, '[data-testid="billing-account-name"]')!;
    expect(input.value).toBe('Frozen name');
    expect(input.readOnly).toBe(true);
    submit(root);

    const retry = http.expectOne({ method: 'POST', url });
    expect(retry.request.body).toEqual({ operationKey: 'frozen-key', name: 'Frozen name' });
    retry.flush(account, { status: 200, statusText: 'OK' });
    http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
  });

  it.each([
    [409, 'Billing.OperationPayloadMismatch', 'billing.payloadMismatch'],
    [403, 'Billing.AccountAccessDenied', 'billing.denied'],
    [400, 'Billing.AccountNameInvalid', 'billing.nameInvalid'],
  ])('ends the attempt on a definitive %i answer', (status, code, message) => {
    const root = render();
    submit(root, 'Acme Billing');

    http.expectOne({ method: 'POST', url }).flush({ code }, { status, statusText: 'Refused' });
    fixture.detectChanges();

    expect(query(root, '[role="alert"]')?.textContent).toContain(message);
    expect(drafts.load(owner)).toBeNull();
    expect(query<HTMLInputElement>(root, '[data-testid="billing-account-name"]')!.readOnly).toBe(false);
  });

  it('does not send a blank name', () => {
    const root = render();
    submit(root, '   ');

    http.expectNone({ method: 'POST', url });
    expect(drafts.load(owner)).toBeNull();
  });

  it('offers a reload when the list cannot be read', () => {
    fixture = TestBed.createComponent(BillingAccountsPageComponent);
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url }).flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    query<HTMLButtonElement>(root, '[data-testid="billing-accounts-reload"]')!.click();
    http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="billing-account-row"]')).toHaveLength(1);
  });

  describe('competing tabs and page instances', () => {
    it('keeps a second tab’s unanswered attempt when the first one succeeds', () => {
      const tabA = open();
      const tabB = open();
      submitIn(tabA, 'Acme Billing');
      submitIn(tabB, 'Bravo');
      const keyB = (posted('Bravo').request.body as { operationKey: string }).operationKey;

      posted('Acme Billing').flush(account, { status: 201, statusText: 'Created' });
      http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
      http.match((req) => req.method === 'POST').forEach((request) => request.error(lost));
      expect(drafts.list(owner)).toEqual([{ ownerUserId: owner, operationKey: keyB, name: 'Bravo' }]);

      const reloaded = open([account]);
      expect(nameInput(reloaded).value).toBe('Bravo');
      expect(nameInput(reloaded).readOnly).toBe(true);
      submitIn(reloaded);
      const retry = posted('Bravo');
      expect(retry.request.body).toEqual({ operationKey: keyB, name: 'Bravo' });
      retry.flush(second, { status: 201, statusText: 'Created' });
      http.expectOne({ method: 'GET', url }).flush({ accounts: [account, second] });
      expect(rows(reloaded)).toBe(2);
      expect(drafts.list(owner)).toEqual([]);
    });

    it('keeps a second tab’s attempt when the first one is refused', () => {
      const tabA = open();
      const tabB = open();
      submitIn(tabA, 'Acme Billing');
      submitIn(tabB, 'Bravo');
      const keyB = (posted('Bravo').request.body as { operationKey: string }).operationKey;

      posted('Acme Billing').flush(
        { code: 'Billing.OperationPayloadMismatch' },
        { status: 409, statusText: 'Conflict' },
      );
      http.match((req) => req.method === 'POST').forEach((request) => request.error(lost));

      expect(drafts.list(owner)).toEqual([{ ownerUserId: owner, operationKey: keyB, name: 'Bravo' }]);
      expect(nameInput(tabA).value).toBe('Bravo');
      expect(nameInput(tabB).value).toBe('Bravo');
    });

    it('a late answer from a left page settles only its own attempt', () => {
      const left = open();
      submitIn(left, 'Acme Billing');
      const original = posted('Acme Billing');
      left.destroy();

      const current = open();
      expect(nameInput(current).value).toBe('Acme Billing');
      submitIn(current);
      posted('Acme Billing').flush(account, { status: 200, statusText: 'OK' });
      http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
      submitIn(current, 'Bravo');
      const bravo = posted('Bravo');
      const keyB = (bravo.request.body as { operationKey: string }).operationKey;

      original.flush(account, { status: 201, statusText: 'Created' });
      http.expectNone({ method: 'GET', url });
      expect(drafts.list(owner)).toEqual([{ ownerUserId: owner, operationKey: keyB, name: 'Bravo' }]);
      expect(nameInput(current).readOnly).toBe(true);
      bravo.flush(second, { status: 201, statusText: 'Created' });
      http.expectOne({ method: 'GET', url }).flush({ accounts: [account, second] });
    });
  });

  describe('list answers racing a creation', () => {
    it('ignores an older empty list that answers after the creation', () => {
      fixture = TestBed.createComponent(BillingAccountsPageComponent);
      fixture.detectChanges();
      const initial = http.expectOne({ method: 'GET', url });
      submitIn(fixture, 'Acme Billing');
      posted('Acme Billing').flush(account, { status: 201, statusText: 'Created' });
      expect(rows(fixture)).toBe(1);

      initial.flush({ accounts: [] });
      expect(rows(fixture)).toBe(1);
      http.expectOne({ method: 'GET', url }).flush({ accounts: [second, account] });
      expect(rows(fixture)).toBe(2);
    });

    it('refreshes after a creation that follows an empty list', () => {
      const instance = open();
      submitIn(instance, 'Acme Billing');
      posted('Acme Billing').flush(account, { status: 201, statusText: 'Created' });
      expect(rows(instance)).toBe(1);

      http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
      expect(rows(instance)).toBe(1);
      expect(query(instance.nativeElement as HTMLElement, '[data-testid="billing-accounts-empty"]')).toBeNull();
    });

    it('keeps the created account and offers a reload when the refresh fails', () => {
      const instance = open();
      submitIn(instance, 'Acme Billing');
      posted('Acme Billing').flush(account, { status: 201, statusText: 'Created' });

      http.expectOne({ method: 'GET', url }).flush({}, { status: 503, statusText: 'Unavailable' });
      expect(rows(instance)).toBe(1);
      const root = instance.nativeElement as HTMLElement;
      query<HTMLButtonElement>(root, '[data-testid="billing-accounts-reload"]')!.click();
      http.expectOne({ method: 'GET', url }).flush({ accounts: [account] });
      expect(rows(instance)).toBe(1);
      expect(query(root, '[data-testid="billing-accounts-reload"]')).toBeNull();
    });
  });
});
