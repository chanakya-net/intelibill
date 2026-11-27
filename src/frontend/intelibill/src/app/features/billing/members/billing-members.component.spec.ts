import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { BillingMembersComponent } from './billing-members.component';

describe('BillingMembersComponent', () => {
  const accountId = 'billing-fixture';
  const accountUrl = `${API_BASE_URL}/billing/accounts/${accountId}`;
  let http: HttpTestingController;

  beforeEach(() => {
    stubMemoryStorage();
    TestBed.configureTestingModule({
      imports: [BillingMembersComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ accountId })) } },
        { provide: AuthService, useValue: { session: signal({ user: { id: 'fixture-owner' } }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  function render(ownerRevision: number | null) {
    const fixture = TestBed.createComponent(BillingMembersComponent);
    fixture.detectChanges();
    http.expectOne(`${accountUrl}/members`).flush({
      accountName: 'Fixture billing account',
      members: [
        { memberId: 'member-a', role: ownerRevision ? 'Owner' : 'Manager', revision: 1, isCurrent: true },
        ...(ownerRevision ? [{ memberId: 'member-b', role: 'Manager', revision: 2, isCurrent: false }] : []),
      ],
      ownerRevision,
      nextCursor: null,
    });
    fixture.detectChanges();
    return fixture;
  }

  it('shows the roster to Managers with no delegation control', () => {
    const root = render(null).nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="billing-member-row"]')?.textContent).toContain('billing.roles.Manager');
    expect(root.querySelector('[data-testid="billing-invite-submit"]')).toBeNull();
  });

  it('repeats the same issue request after an unknown outcome', () => {
    const fixture = render(3);
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLInputElement>('[data-testid="billing-invite-recipient"]')!;
    input.value = 'verified-fixture@example.test';
    const form = root.querySelector('form')!;
    form.dispatchEvent(new Event('submit'));
    const first = http.expectOne({ method: 'POST', url: `${accountUrl}/invitations` });
    expect(first.request.body).toMatchObject({ expectedOwnerRevision: 3, recipientKind: 'email', recipient: input.value });
    first.error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(input.readOnly).toBe(true);

    form.dispatchEvent(new Event('submit'));
    const retry = http.expectOne({ method: 'POST', url: `${accountUrl}/invitations` });
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ operationKey: retry.request.body.operationKey }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(input.value).toBe('');
    expect(root.querySelector('[data-testid="billing-invite-notice"]')).not.toBeNull();
  });

  it('confirms and retries only the frozen manager removal', () => {
    const fixture = render(3);
    const root = fixture.nativeElement as HTMLElement;
    const path = `${accountUrl}/members/member-b/remove`;
    const removal = root.querySelector<HTMLButtonElement>('[data-testid="billing-remove-manager"]');
    expect(removal).not.toBeNull();
    removal!.click();
    fixture.detectChanges();
    document.querySelector<HTMLButtonElement>('[data-testid="billing-remove-cancel"]')!.click();
    fixture.detectChanges();
    http.expectNone(path);

    removal!.click();
    fixture.detectChanges();
    const form = document.querySelector<HTMLFormElement>('[data-testid="billing-remove-form"]')!;
    const reason = form.querySelector('textarea')!;
    reason.value = 'Delegation ended';
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    const first = http.expectOne({ method: 'POST', url: path });
    expect(first.request.body).toMatchObject({ expectedVersion: 2, reason: 'Delegation ended' });
    first.error(new ProgressEvent('error'));
    fixture.destroy();

    const reopened = render(3);
    reopened.nativeElement.querySelector('[data-testid="billing-remove-retry"]').click();
    const retry = http.expectOne({ method: 'POST', url: path });
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ memberId: 'member-b' });
    http.expectOne(`${accountUrl}/members`).flush({
      accountName: 'Fixture billing account', members: [], ownerRevision: 3, nextCursor: null,
    });
    reopened.detectChanges();
    expect(reopened.nativeElement.querySelector('[data-testid="billing-remove-retry"]')).toBeNull();
  });

  it('clears the roster on an account denial', () => {
    const fixture = render(null);
    const root = fixture.nativeElement as HTMLElement;
    window.dispatchEvent(new Event('focus'));
    http.expectOne(`${accountUrl}/members`).flush({ code: 'Billing.AccountAccessDenied' },
      { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="billing-member-row"]')).toBeNull();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('billing.denied');
  });
});
