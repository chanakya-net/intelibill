import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { BehaviorSubject, of } from 'rxjs';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { BillingOwnershipTransferDrafts } from './billing-ownership-transfer-drafts';
import { BillingOwnershipTransferComponent } from './billing-ownership-transfer.component';
import { OWNERSHIP_POLICY } from './billing-ownership-transfer.service';

describe('BillingOwnershipTransferComponent', () => {
  const path = `${API_BASE_URL}/billing/ownership-transfers/proposal`;
  const rosterPath = `${API_BASE_URL}/billing/accounts/account/members`;
  const roster = { accountName: 'Fixture', ownerRevision: 1, members: [] };
  const proposal = { transferId: 'proposal', accountId: 'account', accountName: 'Fixture',
    ownerActorId: 'owner', recipientActorId: 'recipient', revision: 1,
    responsibilityPolicyVersion: OWNERSHIP_POLICY, expiresAt: '2026-11-27T12:00:00Z' };
  const session = signal({ user: { id: 'recipient' } });
  const params = new BehaviorSubject(convertToParamMap({ transferId: 'proposal' }));
  let http: HttpTestingController;

  beforeEach(() => {
    stubMemoryStorage();
    session.set({ user: { id: 'recipient' } });
    params.next(convertToParamMap({ transferId: 'proposal' }));
    TestBed.configureTestingModule({
      imports: [BillingOwnershipTransferComponent, TranslocoTestingModule.forRoot({
        langs: { 'en-IN': { billing: { transfer: {
          recipient: 'Recipient {{recipient}}',
          noLongerAvailable: 'This transfer is no longer available.',
          reviewAgain: 'Billing ownership has changed. Review the transfer again.',
        } } } },
        translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
        preloadLangs: true,
      })],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { session } },
        { provide: ActivatedRoute, useValue: {
          paramMap: params, queryParamMap: of(convertToParamMap({})),
        } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(BillingOwnershipTransferComponent);
    fixture.detectChanges();
    return fixture;
  }

  function confirm(root: HTMLElement, checked: boolean) {
    root.querySelector<HTMLInputElement>('[data-testid="billing-transfer-consent"]')!.checked = checked;
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  }

  function renderOwner() {
    params.next(convertToParamMap({ accountId: 'account' }));
    session.set({ user: { id: 'owner' } });
    const fixture = render();
    http.expectOne(rosterPath).flush(roster);
    fixture.detectChanges();
    return fixture;
  }

  function fillOwnerForm(root: HTMLElement) {
    root.querySelector<HTMLInputElement>('[data-testid="billing-transfer-recipient"]')!.value = 'named@example.test';
    root.querySelector<HTMLTextAreaElement>('[data-testid="billing-transfer-reason"]')!.value = 'Handover';
  }

  function review(root: HTMLElement) {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  }

  function acceptRequest() {
    const fixture = render();
    http.expectOne(path).flush(proposal);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    confirm(root, true);
    return { fixture, root, request: http.expectOne(path + '/accept') };
  }

  function expectSettledAcceptance(root: HTMLElement) {
    expect(TestBed.inject(BillingOwnershipTransferDrafts).list('recipient')).toEqual([]);
    expect(root.querySelector('[data-testid="billing-transfer-lookup"]')).toBeNull();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('This transfer is no longer available.');
  }

  it.each([
    [409, 'Conflict', 'Billing.OwnershipTransferConflict'],
    [404, 'Not Found', 'Billing.OwnershipTransferMissing'],
  ])('settles an accept POST with definitive status %s', (status, statusText, code) => {
    const { fixture, root, request } = acceptRequest();
    request.flush({ code }, { status, statusText });
    fixture.detectChanges();
    expectSettledAcceptance(root);
  });

  it('settles an accept retry when the exact request is refused after a network failure', () => {
    const { fixture, root, request } = acceptRequest();
    request.error(new ProgressEvent('error'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('button[severity="secondary"]')!.click();
    const retry = http.expectOne(path + '/accept');
    expect(retry.request.body).toEqual(request.request.body);
    retry.flush({ code: 'Billing.OwnershipTransferConflict' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expectSettledAcceptance(root);
  });

  it.each([
    [401, 'Unauthorized', 'Auth.SessionInvalid'],
    [409, 'Conflict', 'Billing.OperationPayloadMismatch'],
    [503, 'Service Unavailable', 'Billing.DelegationUnavailable'],
  ])('keeps an accept draft when status %s does not establish its outcome', (status, statusText, code) => {
    const { fixture, root, request } = acceptRequest();
    request.flush({ code }, { status, statusText });
    fixture.detectChanges();
    expect(TestBed.inject(BillingOwnershipTransferDrafts).list('recipient')[0]?.body).toEqual(request.request.body);
    expect(root.querySelector('[data-testid="billing-transfer-lookup"]')).not.toBeNull();
  });

  it('settles a first issue conflict and fetches current owner authority for another review', () => {
    const fixture = renderOwner();
    const root = fixture.nativeElement as HTMLElement;
    fillOwnerForm(root);
    review(root);
    fixture.detectChanges();
    confirm(root, true);
    http.expectOne(`${API_BASE_URL}/billing/accounts/account/ownership-transfers`)
      .flush({ code: 'Billing.OwnershipTransferConflict' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(rosterPath).flush({ ...roster, ownerRevision: 2 });
    fixture.detectChanges();
    expect(TestBed.inject(BillingOwnershipTransferDrafts).list('owner')).toEqual([]);
    expect(root.querySelector('[data-testid="billing-transfer-lookup"]')).toBeNull();
    expect(root.querySelector('[data-testid="billing-transfer-preview"]')).toBeNull();
    expect(root.querySelector('[data-testid="billing-transfer-review"]')).not.toBeNull();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Review the transfer again.');
  });

  it.each([[403, 'Forbidden'], [409, 'Conflict']])('keeps an issue draft when a retry answers %s', (status, statusText) => {
    const issuePath = `${API_BASE_URL}/billing/accounts/account/ownership-transfers`;
    const fixture = renderOwner();
    const root = fixture.nativeElement as HTMLElement;
    fillOwnerForm(root);
    review(root);
    fixture.detectChanges();
    confirm(root, true);
    const first = http.expectOne(issuePath);
    first.error(new ProgressEvent('error'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('button[severity="secondary"]')!.click();
    const retry = http.expectOne(issuePath);
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ code: 'Billing.OwnershipTransferConflict' }, { status, statusText });
    fixture.detectChanges();
    expect(TestBed.inject(BillingOwnershipTransferDrafts).list('owner')[0]?.body).toEqual(first.request.body);
    expect(root.querySelector('[data-testid="billing-transfer-lookup"]')).not.toBeNull();
  });

  it.each(['new owner revision', '403'])('retains owner input and preview on focus until %s', (change) => {
    const fixture = renderOwner();
    const root = fixture.nativeElement as HTMLElement;
    fillOwnerForm(root);
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    http.expectOne(rosterPath).flush(roster);
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('[data-testid="billing-transfer-recipient"]')?.value).toBe('named@example.test');
    expect(root.querySelector<HTMLTextAreaElement>('[data-testid="billing-transfer-reason"]')?.value).toBe('Handover');
    review(root);
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    http.expectOne(rosterPath).flush(roster);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="billing-transfer-preview"]')?.textContent).toContain('named@example.test');

    window.dispatchEvent(new Event('focus'));
    const refresh = http.expectOne(rosterPath);
    if (change === '403') refresh.flush({}, { status: 403, statusText: 'Forbidden' });
    else refresh.flush({ ...roster, ownerRevision: 2 });
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="billing-transfer-preview"]')).toBeNull();
  });

  it('requires consent and retains the exact operation after loss, reload and lookup 404', () => {
    const fixture = render();
    http.expectOne(path).flush(proposal);
    fixture.detectChanges();
    confirm(fixture.nativeElement, false);
    http.expectNone(path + '/accept');
    confirm(fixture.nativeElement, true);
    const first = http.expectOne(path + '/accept');
    expect(first.request.body).toMatchObject({ expectedVersion: 1, acceptResponsibility: true });
    first.error(new ProgressEvent('error'));
    fixture.destroy();

    const reloaded = render();
    const root = reloaded.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[data-testid="billing-transfer-lookup"]')!.click();
    http.expectOne(`${API_BASE_URL}/billing/ownership-transfer-operations/accept/${first.request.body.operationKey}`)
      .flush({ code: 'Billing.OwnershipTransferMissing' }, { status: 404, statusText: 'Not Found' });
    reloaded.detectChanges();
    expect(TestBed.inject(BillingOwnershipTransferDrafts).list('recipient')).toHaveLength(1);
    expect(root.querySelector('[data-testid="billing-transfer-consent"]')).toBeNull();
    root.querySelector<HTMLButtonElement>('button[severity="secondary"]')!.click();
    const retry = http.expectOne(path + '/accept');
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ operationKey: retry.request.body.operationKey,
      accountId: 'account', transferId: 'proposal', effectiveAt: '2026-11-22T12:00:00Z', role: 'Owner' });
    reloaded.detectChanges();
    expect(root.querySelector('[data-testid="billing-transfer-result"]')).not.toBeNull();
  });

  it('ignores a preview from the previous signed-in actor', () => {
    const fixture = render();
    const first = http.expectOne(path);
    session.set({ user: { id: 'other' } });
    fixture.detectChanges();
    first.flush(proposal);
    http.expectOne(path).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="billing-transfer-consent"]')).toBeNull();
  });
});
