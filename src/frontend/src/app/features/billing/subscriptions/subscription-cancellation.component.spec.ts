import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import type { SubscriptionTermStatus } from '../status/subscription-status.models';
import { SubscriptionCancellationComponent } from './subscription-cancellation.component';
import { SubscriptionCancellationDrafts } from './subscription-cancellation-drafts';
import { type CancellationCommand, type CancellationDetail, SubscriptionCancellationService } from './subscription-cancellation.service';

const scope = { actor: 'owner', account: 'account', term: 'term' };
const preview: CancellationDetail = {
  schemaVersion: 1, accountId: scope.account, termId: scope.term, version: 0, changeId: null,
  effectiveAt: '2027-02-28T04:30:00+00:00', policyVersion: 'term-boundary/1', basis: 'Paid',
  commercial: { pricePaise: 149900, currency: 'INR', planVersionId: null },
  sourceHash: 'a'.repeat(64), affectedShopSetHash: 'b'.repeat(64), affectedShopCount: 2,
  affectedShops: [{ shopId: 'first', shopName: 'North' }, { shopId: 'second', shopName: 'South' }],
  nextCursor: null, noRefund: true, noGrace: true,
};
const term: SubscriptionTermStatus = {
  termId: scope.term, state: 'Active', evidenceState: 'Complete', basis: 'Paid', billingPeriod: 'Month', timeZone: 'Asia/Kolkata',
  startsAt: '2027-01-31T04:30:00Z', paidThrough: preview.effectiveAt, graceEndsAt: '2027-03-07T04:30:00Z',
  accessEndsAt: '2027-03-07T04:30:00Z', nextDeadline: preview.effectiveAt, nextPeriodEndsAt: '2027-03-31T04:30:00Z',
  coverageMode: 'Grouped', purchasedCapacity: 2, memberAllowance: 5, sourceReference: 'fixture',
  developmentFixture: true, commercial: preview.commercial,
};

function render() {
  stubMemoryStorage();
  vi.stubGlobal('navigator', { locks: { request: async (_: string, run: () => unknown) => run() } });
  const session = signal({ user: { id: scope.actor } });
  const api = {
    detail: vi.fn(() => Promise.resolve(preview)),
    schedule: vi.fn((_account: string, _term: string, _command: CancellationCommand) => Promise.reject(new HttpErrorResponse({ status: 503 }))),
    recover: vi.fn(() => Promise.reject(new HttpErrorResponse({ status: 404 }))),
  };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      { provide: AuthService, useValue: { session } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: SubscriptionCancellationService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(SubscriptionCancellationComponent);
  fixture.componentRef.setInput('term', term);
  fixture.componentRef.setInput('account', scope.account);
  fixture.detectChanges();
  const element = <T extends HTMLElement>(id: string) => (fixture.nativeElement as HTMLElement)
    .querySelector<T>(`[data-testid="${id}"]`);
  const update = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return { api, element, update, fixture, session };
}

afterEach(() => {
  TestBed.resetTestingModule();
  vi.unstubAllGlobals();
});

describe('scheduled cancellation', () => {
  it('keeps unconfirmed acceptance after lookup 404 and retries the exact command', async () => {
    const { element, update, api } = render();
    const button = (id: string) => element<HTMLButtonElement>('cancellation-' + id)!;
    button('review').click();
    await update();
    expect(element('cancellation-detail')?.textContent).toContain('North');
    expect(element('cancellation-detail')?.textContent).toContain('South');
    expect(button('confirm').disabled).toBe(true);
    element<HTMLInputElement>('cancellation-accept')!.click();
    await update();
    button('confirm').click();
    await update();
    const command = api.schedule.mock.calls[0][2];
    expect(new SubscriptionCancellationDrafts().read(scope)?.command).toEqual(command);
    button('check').click();
    await update();
    expect(api.recover).toHaveBeenCalledWith(scope.account, scope.term, command.operationKey);
    button('retry').click();
    await update();
    expect(api.schedule.mock.calls[1]).toEqual(api.schedule.mock.calls[0]);
  });

  it('drops the previous view on account or actor change and retains the previous operation', async () => {
    const { element, update, fixture, session } = render();
    const draft = { operationKey: 'key', expectedVersion: 0, sourceHash: preview.sourceHash!,
      acceptedEffectiveAt: preview.effectiveAt, affectedShopSetHash: preview.affectedShopSetHash,
      affectedShopCount: 2, policyVersion: preview.policyVersion, confirmed: true as const };
    const store = new SubscriptionCancellationDrafts();
    await store.prepare(scope, draft);
    window.dispatchEvent(new StorageEvent('storage'));
    await update();
    expect(element('cancellation-check')).toBeTruthy();

    fixture.componentRef.setInput('account', 'another-account');
    await update();
    expect(element('cancellation-check')).toBeNull();
    fixture.componentRef.setInput('account', scope.account);
    session.set({ user: { id: 'another-actor' } });
    await update();
    expect(element('cancellation-check')).toBeNull();
    expect(store.read(scope)?.command).toEqual(draft);
  });
});
