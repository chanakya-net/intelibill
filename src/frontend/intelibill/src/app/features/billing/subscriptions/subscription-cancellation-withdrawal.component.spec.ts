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
import { SubscriptionCancellationWithdrawalComponent } from './subscription-cancellation-withdrawal.component';
import { SubscriptionCancellationWithdrawalDrafts } from './subscription-cancellation-withdrawal-drafts';
import type { WithdrawalCommand, WithdrawalOutcome } from './subscription-cancellation.service';
import { SubscriptionCancellationService } from './subscription-cancellation.service';

const scope = { actor: 'owner', account: 'account', term: 'term' };
const effectiveAt = '2027-02-28T04:30:00+00:00';
const term: SubscriptionTermStatus = {
  termId: scope.term, state: 'Active', evidenceState: 'Complete', basis: 'Paid', billingPeriod: 'Month', timeZone: 'Asia/Kolkata',
  startsAt: '2027-01-31T04:30:00Z', paidThrough: effectiveAt, graceEndsAt: null, accessEndsAt: effectiveAt,
  nextDeadline: effectiveAt, nextPeriodEndsAt: '2027-03-31T04:30:00Z', coverageMode: 'Grouped', purchasedCapacity: 2,
  memberAllowance: 5, sourceReference: 'fixture', developmentFixture: true,
  commercial: { pricePaise: 149900, currency: 'INR', planVersionId: null },
  cancellation: { changeId: 'change', version: 1, effectiveAt, policyVersion: 'term-boundary/1' },
};
const outcome = (command: WithdrawalCommand): WithdrawalOutcome => ({
  schemaVersion: 1, accountId: scope.account, termId: scope.term, operationKey: command.operationKey,
  withdrawnChangeId: command.changeId, version: 2, effectiveAt, withdrawnAt: '2027-02-20T04:30:00+00:00', affectedShopCount: 2,
});

function render() {
  stubMemoryStorage();
  vi.stubGlobal('navigator', { locks: { request: async (_: string, run: () => unknown) => run() } });
  const api = {
    withdraw: vi.fn((_account: string, _term: string, _command: WithdrawalCommand): Promise<WithdrawalOutcome> =>
      Promise.reject(new HttpErrorResponse({ status: 0 }))),
    recoverWithdrawal: vi.fn((_account: string, _term: string, _key: string): Promise<WithdrawalOutcome> =>
      Promise.reject(new HttpErrorResponse({ status: 404 }))),
  };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      { provide: AuthService, useValue: { session: signal({ user: { id: scope.actor } }) } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: SubscriptionCancellationService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(SubscriptionCancellationWithdrawalComponent);
  fixture.componentRef.setInput('term', term);
  fixture.componentRef.setInput('account', scope.account);
  fixture.detectChanges();
  const saved = vi.fn();
  fixture.componentInstance.saved.subscribe(saved);
  const element = <T extends HTMLElement>(id: string) => (fixture.nativeElement as HTMLElement)
    .querySelector<T>(`[data-testid="withdrawal-${id}"]`);
  const update = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const confirm = async () => {
    element<HTMLButtonElement>('review')!.click();
    await update();
    expect(element('detail')?.textContent).toContain('2027');
    expect(element<HTMLButtonElement>('confirm')!.disabled).toBe(true);
    element<HTMLInputElement>('accept')!.click();
    await update();
    element<HTMLButtonElement>('confirm')!.click();
    await update();
  };
  return { api, element, update, fixture, saved, confirm };
}

afterEach(() => {
  TestBed.resetTestingModule();
  vi.unstubAllGlobals();
});

describe('cancellation withdrawal', () => {
  it('confirms explicitly, keeps an unknown outcome and settles only on the matching receipt', async () => {
    const { api, element, update, fixture, saved, confirm } = render();
    expect(element('detail')).toBeNull();
    await confirm();
    expect(element('detail')).toBeNull();
    const command = api.withdraw.mock.calls[0][2];
    expect(command).toMatchObject({ currentVersion: 1, changeId: 'change', acceptedEffectiveAt: effectiveAt, confirmed: true });
    expect(new SubscriptionCancellationWithdrawalDrafts().read(scope)).toEqual({ command, settled: false });
    expect(element('pending')).toBeTruthy();
    element<HTMLButtonElement>('check')!.click();
    await update();
    expect(api.recoverWithdrawal).toHaveBeenCalledWith(scope.account, scope.term, command.operationKey);
    expect(element('pending')).toBeTruthy();

    api.withdraw.mockImplementation((_a, _t, sent) => Promise.resolve(outcome(sent)));
    element<HTMLButtonElement>('retry')!.click();
    await update();
    expect(api.withdraw.mock.calls[1]).toEqual(api.withdraw.mock.calls[0]);
    expect(saved).toHaveBeenCalledTimes(1);
    fixture.componentRef.setInput('term', { ...term, cancellation: undefined, graceEndsAt: '2027-03-07T04:30:00Z' });
    await update();
    expect(element('done')?.textContent).toContain('withdrawn');
    expect(element('propagation')).toBeTruthy();
    expect(element('review')).toBeNull();
  });

  it('discards a refused command once the cancellation has taken effect', async () => {
    const { api, element, confirm } = render();
    api.withdraw.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'Billing.CancellationEffective' } }));
    await confirm();
    expect(element('error')?.textContent).toContain('already taken effect');
    expect(new SubscriptionCancellationWithdrawalDrafts().read(scope)).toBeNull();
    expect(element('pending')).toBeNull();
  });

  it('sends nothing when the command cannot be stored first', async () => {
    const { api, element, confirm } = render();
    vi.stubGlobal('navigator', { locks: { request: () => Promise.reject(new Error('storage')) } });
    await confirm();
    expect(api.withdraw).not.toHaveBeenCalled();
    expect(element('error')).toBeTruthy();
  });

  it('does not offer withdrawal once the cancelled term has expired', () => {
    const { element, fixture } = render();
    fixture.componentRef.setInput('term', { ...term, state: 'Expired' });
    fixture.detectChanges();
    expect(element('review')).toBeNull();
  });
});
