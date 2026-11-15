import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { BillingMembersComponent } from './billing-members.component';

describe('BillingMembersComponent', () => {
  const accountId = 'billing-fixture';
  const accountUrl = `${API_BASE_URL}/billing/accounts/${accountId}`;
  let http: HttpTestingController;

  beforeEach(() => {
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

  afterEach(() => http.verify());

  function render(ownerRevision: number | null) {
    const fixture = TestBed.createComponent(BillingMembersComponent);
    fixture.detectChanges();
    http.expectOne(`${accountUrl}/members`).flush({
      accountName: 'Fixture billing account',
      members: [{ memberId: 'member-a', role: ownerRevision ? 'Owner' : 'Manager', revision: 1, isCurrent: true }],
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
});
