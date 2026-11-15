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
import { BillingOwnershipTransferComponent } from './billing-ownership-transfer.component';
import { OWNERSHIP_POLICY } from './billing-ownership-transfer.service';

describe('BillingOwnershipTransferComponent', () => {
  const path = `${API_BASE_URL}/billing/ownership-transfers/proposal`;
  const proposal = { transferId: 'proposal', accountId: 'account', accountName: 'Fixture',
    ownerActorId: 'owner', recipientActorId: 'recipient', revision: 1,
    responsibilityPolicyVersion: OWNERSHIP_POLICY, expiresAt: '2026-11-27T12:00:00Z' };
  const session = signal({ user: { id: 'recipient' } });
  let http: HttpTestingController;

  beforeEach(() => {
    stubMemoryStorage();
    session.set({ user: { id: 'recipient' } });
    TestBed.configureTestingModule({
      imports: [BillingOwnershipTransferComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { session } },
        { provide: ActivatedRoute, useValue: {
          paramMap: of(convertToParamMap({ transferId: 'proposal' })), queryParamMap: of(convertToParamMap({})),
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
      .flush({}, { status: 404, statusText: 'Not Found' });
    reloaded.detectChanges();
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
