import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopSetupPreferences, ShopSetupSample } from '../services/shop-setup.models';
import { ShopSetupService } from '../services/shop-setup.service';
import { ShopSetupSamplePageComponent } from './shop-setup-sample-page.component';

const scope = { shopId: 'shop', userId: 'owner', epoch: 1 };
const sample = { isSample: true, lineType: 'Service', generatedAt: '2026-11-19T00:00:00Z',
  document: { version: 2, number: '' } } as unknown as ShopSetupSample;
const preferences: ShopSetupPreferences = { version: 1, showOnStartup: true, sampleReceiptAcknowledgedAt: null };
const refusal = (code: string) => ({ status: 403, error: { code } });

describe(ShopSetupSamplePageComponent.name, () => {
  const service = { getSample: vi.fn(), getPreferences: vi.fn(), save: vi.fn() };

  function setup() {
    TestBed.configureTestingModule({
      imports: [ShopSetupSamplePageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        provideRouter([]),
        { provide: ShopSetupService, useValue: service },
        { provide: ShopContextService, useValue: {
          scope: signal(scope), epoch: signal(1), isCurrent: () => true, onDetach: () => () => undefined,
        } },
      ],
    });
    TestBed.overrideComponent(ShopSetupSamplePageComponent, { set: { template: '' } });
    return TestBed.createComponent(ShopSetupSamplePageComponent).componentInstance;
  }

  beforeEach(() => {
    vi.resetAllMocks();
    service.getSample.mockReturnValue(of(sample));
    service.getPreferences.mockReturnValue(of(preferences));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  function expectWithdrawn(page: ShopSetupSamplePageComponent): void {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    expect(page.sample()).toBeNull();
    expect(page.preferences()).toBeNull();
    expect(page.failureKey()).toBe('billing.status.suspension.enforcedSecurity');
    page.print();
    expect(print).not.toHaveBeenCalled();
  }

  it('withdraws the receipt when an acknowledgement is refused for security', () => {
    const page = setup();
    service.save.mockReturnValue(throwError(() => refusal('Licence.SecuritySuspended')));

    page.acknowledge();

    expectWithdrawn(page);
    expect(service.getPreferences).toHaveBeenCalledTimes(1);
  });

  it('withdraws the receipt when the preferences read is refused for security', () => {
    service.getPreferences.mockReturnValue(throwError(() => refusal('Licence.SecuritySuspended')));

    expectWithdrawn(setup());
  });

  it('ignores a sample that answers after a security refusal', () => {
    const late = new Subject<ShopSetupSample>();
    service.getSample.mockReturnValue(late);
    service.getPreferences.mockReturnValue(throwError(() => refusal('Licence.SecuritySuspended')));
    const page = setup();

    late.next(sample);

    expectWithdrawn(page);
  });

  it('ignores an acknowledgement that answers after a security refusal', () => {
    const saving = new Subject<ShopSetupPreferences>();
    service.save.mockReturnValue(saving);
    const preferencesRead = new Subject<ShopSetupPreferences>();
    const page = setup();
    page.acknowledge();
    service.getPreferences.mockReturnValue(preferencesRead);
    page.refresh();
    preferencesRead.error(refusal('Licence.SecuritySuspended'));

    saving.next({ ...preferences, version: 2 });

    expect(page.preferences()).toBeNull();
    expect(page.saving()).toBe(false);
  });

  it('shows the security refusal for the sample read itself and recovers on an explicit refresh', () => {
    service.getSample.mockReturnValueOnce(throwError(() => refusal('Licence.SecuritySuspended')));
    const page = setup();
    expectWithdrawn(page);

    page.refresh();

    expect(page.sample()).toEqual(sample);
    expect(page.failureKey()).toBeNull();
  });

  it('keeps the sample readable after an ordinary trading refusal', () => {
    const page = setup();
    service.save.mockReturnValue(throwError(() => refusal('Licence.TradingRestricted')));

    page.acknowledge();

    expect(page.sample()).toEqual(sample);
    expect(page.preferences()).toEqual(preferences);
    expect(page.saveError()).toBe('billing.status.tradingAuthority.restricted');
    expect(service.getPreferences).toHaveBeenCalledTimes(2);
  });
});
